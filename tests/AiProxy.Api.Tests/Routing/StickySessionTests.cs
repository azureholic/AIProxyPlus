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

public class StickySessionTests : IAsyncLifetime, IDisposable
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

    private static Endpoint NewApiKeyEndpoint(Guid deploymentId, string baseUrl, int priority) =>
        Endpoint.Create(
            Guid.NewGuid(), deploymentId, baseUrl, ApiFamily.OpenAI, priority, AuthMode.ApiKey,
            secretRef: "endpoints/x", maxRetries: 0);

    private static object ChatBody(string model, string session) => new
    {
        model,
        session,
        messages = new[] { new { role = "user", content = "hi" } },
    };

    [Fact]
    public async Task RepeatedCalls_WithSameSession_AlwaysHitSameBackend()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var sessionId = $"session-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName, stickySessionEnabled: true);
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
            var response = await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName, sessionId));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // All 5 calls should have landed on whichever endpoint was picked first (priority 1) — none on the other.
        (_backend1.LogEntries.Count() == 5 || _backend2.LogEntries.Count() == 5).Should().BeTrue();
        (_backend1.LogEntries.Any() && _backend2.LogEntries.Any()).Should().BeFalse();
    }

    [Fact]
    public async Task WhenStickyEndpointFails_SubsequentCallsFailFast_WithoutFallingBackToOtherEndpoint()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var sessionId = $"session-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName, stickySessionEnabled: true);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .InScenario("flaky")
            .WillSetStateTo("failed")
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-1" }));
        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .InScenario("flaky")
            .WhenStateIs("failed")
            .RespondWith(Response.Create().WithStatusCode(500));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2" }));

        var client = _factory.CreateClient();
        var first = await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName, sessionId));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName, sessionId));

        second.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _backend2.LogEntries.Should().BeEmpty("a failed sticky endpoint must fail fast, not silently reroute a stateful session");
    }

    [Fact]
    public async Task DifferentSessions_CanLandOnDifferentEndpoints()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName, stickySessionEnabled: true);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-1" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2" }));

        var client = _factory.CreateClient();
        var responseA = await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName, "session-a"));
        var responseB = await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName, "session-b"));

        responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);
        // Both should land on priority-1 backend1 since it's healthy for both independent first calls.
        _backend1.LogEntries.Should().HaveCount(2);
    }
}
