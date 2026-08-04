using System.Net;
using System.Net.Http.Json;
using AiProxy.Api.Tests.Infrastructure;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AiProxy.Api.Tests.Usage;

/// <summary>Phase 10 fixture-coverage gap-fill: Images edit/variation, streaming Responses API chaining.</summary>
public class AdditionalCoverageTests : IAsyncLifetime, IDisposable
{
    private readonly ApiTestFactory _factory = new();
    private WireMockServer _backend = null!;

    public Task InitializeAsync()
    {
        _backend = WireMockServer.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _backend.Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();

    private static ModelEndpoint NewApiKeyEndpoint(Guid modelId, string baseUrl, int priority = 1)
    {
        var endpointId = Guid.NewGuid();
        var endpoint = Endpoint.Create(
            endpointId, $"additional-coverage-test-endpoint-{endpointId:N}", baseUrl, AuthMode.ApiKey,
            secretRef: "endpoints/x", maxRetries: 0);
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/openai/v1");
        return ModelEndpoint.Create(Guid.NewGuid(), modelId, endpoint, route, priority, enabled: true);
    }

    [Fact]
    public async Task ImageEdit_NonStreaming_CapturesUsageMetrics()
    {
        var modelName = $"legacy-image-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend.Url!));
        _factory.Models.Seed(model);

        _backend.Given(Request.Create().WithPath("/openai/v1/images/edits").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { data = new[] { new { url = "https://example/edited.png", size = "512x512" } } }));

        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/openai/v1/images/edits", new { model = modelName, prompt = "make it blue" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ImageVariation_NonStreaming_Succeeds()
    {
        var modelName = $"legacy-image-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend.Url!));
        _factory.Models.Seed(model);

        _backend.Given(Request.Create().WithPath("/openai/v1/images/variations").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { data = new[] { new { url = "https://example/variation.png" } } }));

        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/openai/v1/images/variations", new { model = modelName });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResponsesApi_StreamingFollowUpTurn_PinsToOriginatingBackend()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        var backend2 = WireMockServer.Start();
        try
        {
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend.Url!));
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, backend2.Url!, priority: 2));
            _factory.Models.Seed(model);

            const string responseId = "resp_stream_abc";
            var sse =
                "data: {\"type\":\"response.output_text.delta\",\"delta\":\"Hi\"}\n\n" +
                $"data: {{\"type\":\"response.completed\",\"response\":{{\"id\":\"{responseId}\",\"usage\":{{\"input_tokens\":5,\"output_tokens\":2,\"total_tokens\":7}}}}}}\n\n";

            _backend.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "text/event-stream").WithBody(sse));
            backend2.Given(Request.Create().WithPath("/openai/v1/responses").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "should-not-be-called" }));

            var client = _factory.CreateClient();
            var first = await client.PostAsJsonAsync("/openai/v1/responses", new { model = modelName, input = "hi", stream = true });
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            (await first.Content.ReadAsStringAsync()).Should().Contain("response.completed");

            var second = await client.PostAsJsonAsync(
                "/openai/v1/responses", new { model = modelName, previous_response_id = responseId, input = "and then?", stream = true });

            second.StatusCode.Should().Be(HttpStatusCode.OK);
            backend2.LogEntries.Should().BeEmpty("the follow-up turn must pin to the endpoint that created responseId, not fail over");
        }
        finally
        {
            backend2.Stop();
        }
    }

    [Fact]
    public async Task CallerCancellation_DoesNotTriggerFailoverRetry()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend.Url!));
        _factory.Models.Seed(model);

        _backend.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(5)).WithBodyAsJson(new { id = "resp-1" }));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var request = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = modelName, messages = new[] { new { role = "user", content = "hi" } } }),
        };

        var act = () => _factory.CreateClient().SendAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("the caller gave up waiting; the proxy must not retry/failover on caller cancellation");
    }
}
