using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using AiProxy.Api.Tests.Infrastructure;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Observability;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AiProxy.Api.Tests.Observability;

/// <summary>
/// Automated stand-in for the Phase 8 "run via Aspire, confirm traces/metrics appear in the dashboard" manual
/// verification step: listens directly to the <see cref="AiProxyActivitySource"/>/<see cref="AiProxyMetrics"/>
/// instruments during a real routed request instead of relying on visual dashboard inspection.
/// </summary>
public class TelemetryTests : IAsyncLifetime, IDisposable
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

    private static ModelEndpoint NewApiKeyEndpoint(Guid modelId, string baseUrl, int priority)
    {
        var endpointId = Guid.NewGuid();
        var endpoint = Endpoint.Create(
            endpointId, $"telemetry-test-endpoint-{endpointId:N}", baseUrl, ApiFamily.OpenAI, AuthMode.ApiKey,
            secretRef: "endpoints/x", maxRetries: 0);
        return ModelEndpoint.Create(modelId, endpoint, priority, enabled: true);
    }

    [Fact]
    public async Task SuccessfulRequest_EmitsExpectedActivitiesAndMetrics()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend.Url!, priority: 1));
        _factory.Models.Seed(model);

        _backend.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { id = "resp-1", usage = new { prompt_tokens = 3, completion_tokens = 4, total_tokens = 7 } }));

        var activityNames = new List<string>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AiProxyActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => { lock (activityNames) { activityNames.Add(activity.OperationName); } },
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new List<(string Instrument, object Value)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AiProxyMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            lock (measurements) { measurements.Add((instrument.Name, value)); }
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            lock (measurements) { measurements.Add((instrument.Name, value)); }
        });
        meterListener.Start();

        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = modelName, messages = new[] { new { role = "user", content = "hi" } } });
        response.EnsureSuccessStatusCode();

        activityNames.Should().Contain(["ai_proxy.request", "model_resolution", "secret_resolution", "backend_call", "usage_extraction"]);
        measurements.Select(m => m.Instrument).Should().Contain(
            ["ai_proxy.requests.count", "ai_proxy.request.duration", "ai_proxy.tokens.prompt", "ai_proxy.tokens.completion", "ai_proxy.tokens.total"]);
    }

    [Fact]
    public async Task Failover_EmitsFailoverCountMetric()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        var backend2 = WireMockServer.Start();
        try
        {
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend.Url!, priority: 1));
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, backend2.Url!, priority: 2));
            _factory.Models.Seed(model);

            _backend.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(500));
            backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2" }));

            var measurements = new List<string>();
            using var meterListener = new MeterListener();
            meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AiProxyMetrics.MeterName && instrument.Name == "ai_proxy.failover.count")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, _, _, _) =>
            {
                lock (measurements) { measurements.Add(instrument.Name); }
            });
            meterListener.Start();

            var response = await _factory.CreateClient().PostAsJsonAsync(
                "/openai/v1/chat/completions", new { model = modelName, messages = new[] { new { role = "user", content = "hi" } } });
            response.EnsureSuccessStatusCode();

            measurements.Should().Contain("ai_proxy.failover.count");
        }
        finally
        {
            backend2.Stop();
        }
    }
}
