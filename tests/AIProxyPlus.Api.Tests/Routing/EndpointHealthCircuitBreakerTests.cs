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

/// <summary>
/// Proves the cross-request passive circuit breaker (architecture.md §9, Iteration 12): an endpoint that just
/// failed is skipped on a subsequent, separate request while within the tracker's cooldown, then becomes
/// eligible again once the cooldown elapses.
/// </summary>
public class EndpointHealthCircuitBreakerTests : IAsyncLifetime, IDisposable
{
    private readonly ApiTestFactory _factory = new();
    private WireMockServer _failingBackend = null!;
    private WireMockServer _healthyBackend = null!;

    public Task InitializeAsync()
    {
        _failingBackend = WireMockServer.Start();
        _healthyBackend = WireMockServer.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _failingBackend.Stop();
        _healthyBackend.Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();

    private static ModelEndpoint NewApiKeyEndpoint(Guid modelId, string baseUrl, int priority) =>
        ModelEndpoint.Create(
            Guid.NewGuid(),
            modelId,
            Endpoint.Create(
                Guid.NewGuid(), $"circuit-breaker-test-endpoint-{Guid.NewGuid():N}", baseUrl, AuthMode.ApiKey,
                secretRef: "endpoints/x", maxRetries: 0),
            ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/openai/v1"),
            priority,
            enabled: true);

    private static object ChatBody(string model) => new { model, messages = new[] { new { role = "user", content = "hi" } } };

    [Fact]
    public async Task EndpointReturning500_IsNotReplayedForThatRequest_ButIsSkippedOnTheNext()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _failingBackend.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _healthyBackend.Url!, priority: 2));
        _factory.Models.Seed(model);

        _failingBackend.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));
        _healthyBackend.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-1" }));

        var first = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));
        first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        _healthyBackend.LogEntries.Should().BeEmpty("a possibly-processed request must not be replayed elsewhere");

        var second = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        _failingBackend.LogEntries.Should().ContainSingle("the failed endpoint is skipped while it cools down");
    }

    [Fact]
    public async Task FailedEndpoint_IsSkippedOnSubsequentRequest_ThenEligibleAgainAfterCooldown()
    {
        // Different priorities (rather than a shared priority) make failover order deterministic — otherwise
        // which endpoint round-robin tries first is a coin flip based on Guid ordering.
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _failingBackend.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _healthyBackend.Url!, priority: 2));
        _factory.Models.Seed(model);

        _failingBackend.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));
        _healthyBackend.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-1" }));

        using var client = _factory.WithWebHostBuilder(
                builder => builder.UseSetting("Routing:UnhealthyCooldown", "00:00:00.300"))
            .CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiTestFactory.DefaultConsumerApiKey);

        // Request 1: same-request failover tries the failing endpoint once, then succeeds against the healthy one.
        var first = await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        _failingBackend.LogEntries.Count().Should().Be(1);

        // Request 2, immediately after: the failing endpoint is still within its cooldown window, so it must be
        // skipped entirely rather than retried from a cold start.
        var second = await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        _failingBackend.LogEntries.Count().Should().Be(1, "the failing endpoint's cooldown has not elapsed yet");
        _healthyBackend.LogEntries.Count().Should().Be(2);

        // Once the cooldown elapses, the endpoint becomes eligible again (self-healing, no manual reset needed).
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        var third = await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));
        third.StatusCode.Should().Be(HttpStatusCode.OK);
        _failingBackend.LogEntries.Count().Should().Be(2, "the cooldown elapsed, so the endpoint is tried again");
    }
}
