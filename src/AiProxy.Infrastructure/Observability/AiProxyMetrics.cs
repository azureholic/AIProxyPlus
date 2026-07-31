using System.Diagnostics.Metrics;

namespace AiProxy.Infrastructure.Observability;

/// <summary>Custom `Meter("AiProxy")` instruments (architecture.md §11).</summary>
public sealed class AiProxyMetrics : IDisposable
{
    public const string MeterName = "AiProxy";

    private readonly Meter _meter;
    private readonly Counter<long> _requestsCount;
    private readonly Counter<long> _promptTokens;
    private readonly Counter<long> _completionTokens;
    private readonly Counter<long> _totalTokens;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _failoverCount;

    public AiProxyMetrics()
    {
        _meter = new Meter(MeterName);
        _requestsCount = _meter.CreateCounter<long>("ai_proxy.requests.count", description: "Ingress requests handled, by outcome.");
        _promptTokens = _meter.CreateCounter<long>("ai_proxy.tokens.prompt", description: "Prompt tokens captured.");
        _completionTokens = _meter.CreateCounter<long>("ai_proxy.tokens.completion", description: "Completion tokens captured.");
        _totalTokens = _meter.CreateCounter<long>("ai_proxy.tokens.total", description: "Total tokens captured.");
        _requestDuration = _meter.CreateHistogram<double>("ai_proxy.request.duration", unit: "ms", description: "Ingress request duration.");
        _failoverCount = _meter.CreateCounter<long>("ai_proxy.failover.count", description: "Failovers from one endpoint to the next.");
    }

    public void RecordRequest(string deployment, string apiFamily, string endpoint, int statusCode) =>
        _requestsCount.Add(
            1,
            new KeyValuePair<string, object?>("deployment", deployment),
            new KeyValuePair<string, object?>("api_family", apiFamily),
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("status", statusCode));

    public void RecordTokens(string deployment, string endpoint, int? promptTokens, int? completionTokens, int? totalTokens)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("deployment", deployment),
            new("endpoint", endpoint),
        };

        if (promptTokens is { } prompt)
        {
            _promptTokens.Add(prompt, tags);
        }

        if (completionTokens is { } completion)
        {
            _completionTokens.Add(completion, tags);
        }

        if (totalTokens is { } total)
        {
            _totalTokens.Add(total, tags);
        }
    }

    public void RecordDuration(double milliseconds, string deployment, string apiFamily, string endpoint, int statusCode) =>
        _requestDuration.Record(
            milliseconds,
            new KeyValuePair<string, object?>("deployment", deployment),
            new KeyValuePair<string, object?>("api_family", apiFamily),
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("status", statusCode));

    public void RecordFailover(string deployment, string fromEndpoint, string toEndpoint) =>
        _failoverCount.Add(
            1,
            new KeyValuePair<string, object?>("deployment", deployment),
            new KeyValuePair<string, object?>("from_endpoint", fromEndpoint),
            new KeyValuePair<string, object?>("to_endpoint", toEndpoint));

    public void Dispose() => _meter.Dispose();
}
