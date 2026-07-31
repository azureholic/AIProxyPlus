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

public class ResponsesApiAffinityTests : IAsyncLifetime, IDisposable
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

    [Fact]
    public async Task FollowUpTurn_WithPreviousResponseId_PinsToOriginatingBackend()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName, stickySessionEnabled: true);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        const string responseId = "resp_abc123";
        _backend1.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = responseId, output = Array.Empty<object>() }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "should-not-be-called", output = Array.Empty<object>() }));

        var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync("/openai/v1/responses", new { model = deploymentName, input = "hello" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync(
            "/openai/v1/responses", new { model = deploymentName, previous_response_id = responseId, input = "and then?" });

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        _backend1.LogEntries.Should().HaveCount(2);
        _backend2.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task FollowUpTurn_WithUnknownPreviousResponseId_FallsBackToNormalRouting()
    {
        // An unrecognized (e.g. expired/evicted) previous_response_id has no affinity entry, so there is no
        // sticky endpoint to pin to or fail over from — the request is simply routed like a fresh one.
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName, stickySessionEnabled: true);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp_fresh" }));

        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/openai/v1/responses", new { model = deploymentName, previous_response_id = "resp_never-seen", input = "hi" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WithStoreFalse_DoesNotPersistAffinityForFollowUp()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName, stickySessionEnabled: true);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        _factory.Deployments.Seed(deployment);

        const string responseId = "resp_no-store";
        _backend1.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = responseId }));

        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/openai/v1/responses", new { model = deploymentName, input = "hi", store = false });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _factory.Affinity.GetAsync(responseId)).Should().BeNull();
    }
}
