using System.Net;
using System.Net.Http.Json;
using AIProxyPlus.Api.Tests.Infrastructure;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AIProxyPlus.Api.Tests.Routing;

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

    private static ModelEndpoint NewApiKeyEndpoint(Guid modelId, string baseUrl, int priority, int maxRetries = 0)
    {
        var endpointId = Guid.NewGuid();
        var endpoint = Endpoint.Create(
            endpointId, $"chat-completion-test-endpoint-{endpointId:N}", baseUrl, AuthMode.ApiKey,
            secretRef: "endpoints/x", maxRetries: maxRetries);
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/openai/v1");
        return ModelEndpoint.Create(Guid.NewGuid(), modelId, endpoint, route, priority, enabled: true);
    }

    private static object ChatBody(string model) => new { model, messages = new[] { new { role = "user", content = "hi" } } };

    [Fact]
    public async Task Routes_ToLowestPriorityHealthyEndpoint()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-1", choices = Array.Empty<object>() }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resp-1");
        _backend2.LogEntries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(503)]
    [InlineData(529)]
    public async Task Failover_OnBackendRejectionBeforeProcessing_TriesNextPriorityEndpoint(int statusCode)
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(statusCode));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resp-2");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(504)]
    public async Task BackendErrorThatMayHaveProcessedTheRequest_IsNeitherRetriedNorFailedOver(int statusCode)
    {
        // A 500/502/504 does not say the upstream skipped the work, so replaying this POST could duplicate it.
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1, maxRetries: 3));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(statusCode).WithBodyAsJson(new { error = "upstream failed after accepting" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

        ((int)response.StatusCode).Should().Be(statusCode);
        (await response.Content.ReadAsStringAsync()).Should().Contain("upstream failed after accepting");
        _backend1.LogEntries.Should().ContainSingle("the same endpoint must not be retried");
        _backend2.LogEntries.Should().BeEmpty("the request must not be failed over to another endpoint");
    }

    [Fact]
    public async Task Backend503_IsRetriedOnTheSameEndpointBeforeFailingOver()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1, maxRetries: 2));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _backend1.LogEntries.Should().HaveCount(3, "one attempt plus two bounded retries");
    }

    [Fact]
    public async Task Failover_OnConnectionRefused_TriesNextPriorityEndpoint()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, "http://127.0.0.1:1", priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resp-2");
    }

    [Fact]
    public async Task AllEndpointsUnhealthy_ReturnsFirstEndpointsOriginalError()
    {
        // The client sees the priority-1 endpoint's own error (503), not a generic one that masks which
        // backend actually failed and why (architecture.md §9) — even though priority-2 failed differently (429).
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503).WithBodyAsJson(new { error = "backend1 exploded" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain("backend1 exploded");
    }

    [Fact]
    public async Task UnknownModel_Returns404()
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
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        var disabled = NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1);
        disabled.Disable();
        model.AddEndpoint(disabled);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _backend1.LogEntries.Should().BeEmpty();
    }
}
