using System.Diagnostics.Metrics;

namespace AIProxyPlus.Infrastructure.Observability;

/// <summary>Custom `Meter("AIProxyPlus")` instruments (architecture.md §11).</summary>
public sealed class AIProxyPlusMetrics : IDisposable
{
    public const string MeterName = "AIProxyPlus";

    private readonly Meter _meter;
    private readonly Counter<long> _requestsCount;
    private readonly Counter<long> _promptTokens;
    private readonly Counter<long> _completionTokens;
    private readonly Counter<long> _totalTokens;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _failoverCount;
    private readonly Counter<long> _credentialFallbackCount;
    private readonly Counter<long> _credentialInvalidationNotificationCount;
    private readonly Counter<long> _credentialRetirementCount;

    public AIProxyPlusMetrics()
    {
        _meter = new Meter(MeterName);
        _requestsCount = _meter.CreateCounter<long>("aiproxyplus.requests.count", description: "Ingress requests handled, by outcome.");
        _promptTokens = _meter.CreateCounter<long>("aiproxyplus.tokens.prompt", description: "Prompt tokens captured.");
        _completionTokens = _meter.CreateCounter<long>("aiproxyplus.tokens.completion", description: "Completion tokens captured.");
        _totalTokens = _meter.CreateCounter<long>("aiproxyplus.tokens.total", description: "Total tokens captured.");
        _requestDuration = _meter.CreateHistogram<double>("aiproxyplus.request.duration", unit: "ms", description: "Ingress request duration.");
        _failoverCount = _meter.CreateCounter<long>("aiproxyplus.failover.count", description: "Failovers from one endpoint to the next.");
        _credentialFallbackCount = _meter.CreateCounter<long>("aiproxyplus.credential_fallback.count", description: "Credential fallback attempts by outcome.");
        _credentialInvalidationNotificationCount = _meter.CreateCounter<long>(
            "aiproxyplus.credential_invalidation_notification.count",
            description: "Credential invalidation notification activity by operation and outcome.");
        _credentialRetirementCount = _meter.CreateCounter<long>(
            "aiproxyplus.credential_retirement.count",
            description: "Credential retirement work by reason and outcome.");
    }

    public void RecordRequest(string model, string provider, string endpoint, int statusCode) =>
        _requestsCount.Add(
            1,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("status", statusCode));

    public void RecordTokens(string model, string endpoint, int? promptTokens, int? completionTokens, int? totalTokens)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("model", model),
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

    public void RecordDuration(double milliseconds, string model, string provider, string endpoint, int statusCode) =>
        _requestDuration.Record(
            milliseconds,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("status", statusCode));

    public void RecordFailover(string model, string fromEndpoint, string toEndpoint) =>
        _failoverCount.Add(
            1,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("from_endpoint", fromEndpoint),
            new KeyValuePair<string, object?>("to_endpoint", toEndpoint));

    public void RecordCredentialFallback(string provider, Guid endpointId, string endpointName, string outcome) =>
        _credentialFallbackCount.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("endpoint_id", endpointId),
            new KeyValuePair<string, object?>("endpoint_name", endpointName),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordCredentialInvalidationNotification(string operation, string outcome) =>
        _credentialInvalidationNotificationCount.Add(
            1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordCredentialRetirement(string reason, string outcome) =>
        _credentialRetirementCount.Add(
            1,
            new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void Dispose() => _meter.Dispose();
}
