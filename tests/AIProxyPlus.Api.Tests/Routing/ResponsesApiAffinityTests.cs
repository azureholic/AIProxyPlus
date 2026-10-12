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

    private static ModelEndpoint NewApiKeyEndpoint(Guid modelId, string baseUrl, int priority)
    {
        var endpointId = Guid.NewGuid();
        var endpoint = Endpoint.Create(
            endpointId, $"responses-affinity-test-endpoint-{endpointId:N}", baseUrl, AuthMode.ApiKey,
            secretRef: "endpoints/x", maxRetries: 0);
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/openai/v1");
        return ModelEndpoint.Create(Guid.NewGuid(), modelId, endpoint, route, priority, enabled: true);
    }

    [Fact]
    public async Task FollowUpTurn_WithPreviousResponseId_PinsToOriginatingBackend()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        const string responseId = "resp_abc123";
        _backend1.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = responseId, output = Array.Empty<object>() }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "should-not-be-called", output = Array.Empty<object>() }));

        var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync("/openai/v1/responses", new { model = modelName, input = "hello" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync(
            "/openai/v1/responses", new { model = modelName, previous_response_id = responseId, input = "and then?" });

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        _backend1.LogEntries.Should().HaveCount(2);
        _backend2.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task FollowUpTurn_WithUnknownPreviousResponseId_FallsBackToNormalRouting()
    {
        // An unrecognized (e.g. expired/evicted) previous_response_id has no affinity entry, so there is no
        // sticky endpoint to pin to or fail over from — the request is simply routed like a fresh one.
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        _factory.Models.Seed(model);

        _backend1.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp_fresh" }));

        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/openai/v1/responses", new { model = modelName, previous_response_id = "resp_never-seen", input = "hi" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WithStoreFalse_DoesNotPersistAffinityForFollowUp()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        _factory.Models.Seed(model);

        const string responseId = "resp_no-store";
        _backend1.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = responseId }));

        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/openai/v1/responses", new { model = modelName, input = "hi", store = false });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _factory.Affinity.GetAsync(responseId)).Should().BeNull();
    }

    [Fact]
    public async Task WhenOriginatingEndpointFailsOnFollowUp_FailsFast_WithoutFallingBackToOtherEndpoint()
    {
        // Responses API affinity is unconditional (no model config needed) and, like any sticky session,
        // must fail fast rather than silently reroute a stateful conversation to a different backend.
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        const string responseId = "resp_flaky";
        _backend1.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = responseId }));

        var client = _factory.CreateClient();
        var first = await client.PostAsJsonAsync("/openai/v1/responses", new { model = modelName, input = "hi" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        _backend1.Reset();
        _backend1.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));
        _backend2.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "should-not-be-called" }));

        var second = await client.PostAsJsonAsync(
            "/openai/v1/responses", new { model = modelName, previous_response_id = responseId, input = "and then?" });

        second.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _backend2.LogEntries.Should().BeEmpty("a failed sticky endpoint must fail fast, not silently reroute a stateful conversation");
    }

    [Fact]
    public async Task ConfiguredSessionAffinityTtl_IsAppliedToTheAffinityRow()
    {
        // Iteration 12: the previously-hardcoded 30-minute TTL is now bound from Routing:SessionAffinityTtl.
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        _factory.Models.Seed(model);

        const string responseId = "resp_ttl-config";
        _backend1.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = responseId, output = Array.Empty<object>() }));

        using var client = _factory.WithWebHostBuilder(
                builder => builder.UseSetting("Routing:SessionAffinityTtl", "00:00:05"))
            .CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiTestFactory.DefaultConsumerApiKey);

        var response = await client.PostAsJsonAsync("/openai/v1/responses", new { model = modelName, input = "hello" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entry = await _factory.Affinity.GetAsync(responseId);
        entry.Should().NotBeNull();
        (entry!.ExpiresAt - entry.LastUsedAt).Should().BeCloseTo(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
    }
}
