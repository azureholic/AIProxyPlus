using System.Net;
using System.Net.Http.Json;
using AiProxy.Api.Tests.Infrastructure;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AiProxy.Api.Tests.Routing;

public class ChatCompletionRoutingTests : IAsyncLifetime, IDisposable
{
    private readonly ApiTestFactory _factory = new();
    private WireMockServer _backend1 = null!;
    private WireMockServer _backend2 = null!;

    public Task InitializeAsync()
    {
        _backend1 = WireMockServer.Start();
        _backend2 = WireMockServer.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _backend1.Stop();
        _backend2.Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();

    private static ModelDeploymentEndpoint NewApiKeyEndpoint(Guid deploymentId, string baseUrl, int priority, int maxRetries = 0)
    {
        var endpointId = Guid.NewGuid();
        var endpoint = Endpoint.Create(
            endpointId, $"chat-completion-test-endpoint-{endpointId:N}", baseUrl, ApiFamily.OpenAI, AuthMode.ApiKey,
            secretRef: "endpoints/x", maxRetries: maxRetries);
        return ModelDeploymentEndpoint.Create(deploymentId, endpoint, priority, enabled: true);
    }

    private static object ChatBody(string model) => new { model, messages = new[] { new { role = "user", content = "hi" } } };

    [Fact]
    public async Task Routes_ToLowestPriorityHealthyEndpoint()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-1", choices = Array.Empty<object>() }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resp-1");
        _backend2.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Failover_OnBackend5xx_TriesNextPriorityEndpoint()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resp-2");
    }

    [Fact]
    public async Task Failover_OnConnectionRefused_TriesNextPriorityEndpoint()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, "http://127.0.0.1:1", priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resp-2");
    }

    [Fact]
    public async Task AllEndpointsUnhealthy_ReturnsFirstEndpointsOriginalError()
    {
        // The client sees the priority-1 endpoint's own error (500), not a generic 503 that masks which
        // backend actually failed and why (architecture.md §9) — even though priority-2 failed differently (503).
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBodyAsJson(new { error = "backend1 exploded" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("backend1 exploded");
    }

    [Fact]
    public async Task UnknownDeployment_Returns404()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody("never-registered"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MissingModelField_Returns404()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", new { messages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DisabledEndpoint_IsNeverSelected()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        var disabled = NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1);
        disabled.Disable();
        deployment.AddEndpoint(disabled);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _backend1.LogEntries.Should().BeEmpty();
    }
}
