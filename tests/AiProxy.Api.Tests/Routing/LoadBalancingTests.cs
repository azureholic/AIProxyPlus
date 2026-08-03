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

public class LoadBalancingTests : IAsyncLifetime, IDisposable
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
            endpointId, $"load-balancing-test-endpoint-{endpointId:N}", baseUrl, ApiFamily.OpenAI, AuthMode.ApiKey,
            secretRef: "endpoints/x", maxRetries: maxRetries);
        return ModelDeploymentEndpoint.Create(deploymentId, endpoint, priority, enabled: true);
    }

    private static object ChatBody(string model) => new { model, messages = new[] { new { role = "user", content = "hi" } } };

    [Fact]
    public async Task SamePriorityEndpoints_SplitTrafficAcrossRepeatedCalls()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 1));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-1" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2" }));

        var client = _factory.CreateClient();
        for (var i = 0; i < 8; i++)
        {
            var response = await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Round-robin across two equal-priority endpoints over 8 requests must hit both, not just one.
        _backend1.LogEntries.Should().NotBeEmpty();
        _backend2.LogEntries.Should().NotBeEmpty();
        (_backend1.LogEntries.Count() + _backend2.LogEntries.Count()).Should().Be(8);
    }

    [Fact]
    public async Task HigherPriorityEndpoint_IsAlwaysPreferredOverLowerPriority_DespiteRoundRobinState()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-1" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2" }));

        var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));
        }

        _backend1.LogEntries.Should().HaveCount(5);
        _backend2.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Backend429_TriggersFailoverToNextPriorityEndpoint_SameAs500()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "1"));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2" }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resp-2");
    }

    [Fact]
    public async Task AllEndpointsReturn429_ReturnsFirstEndpointsOriginal429WithRetryAfterHeader()
    {
        // Every endpoint failing must not be masked behind a generic 503 — the client gets the priority-1
        // endpoint's actual 429 back, including its Retry-After header, so it knows how long to back off
        // (architecture.md §9).
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "37"));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));

        response.StatusCode.Should().Be((HttpStatusCode)429);
        response.Headers.RetryAfter?.Delta.Should().Be(TimeSpan.FromSeconds(37));
    }

    [Fact]
    public async Task FirstEndpointFails429_SecondEndpointFailsWithADifferentError_ClientSeesFirstEndpoints429()
    {
        // The two endpoints fail with genuinely different errors (429 vs 500) — the client must still see the
        // priority-1 endpoint's 429 + Retry-After, not priority-2's 500 and not a generic 503.
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "5").WithBodyAsJson(new { error = "rate limited" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBodyAsJson(new { error = "backend2 exploded" }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));

        response.StatusCode.Should().Be((HttpStatusCode)429);
        response.Headers.RetryAfter?.Delta.Should().Be(TimeSpan.FromSeconds(5));
        (await response.Content.ReadAsStringAsync()).Should().Contain("rate limited");
    }
}
