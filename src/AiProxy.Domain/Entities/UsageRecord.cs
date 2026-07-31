using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;

namespace AiProxy.Domain.Entities;

/// <summary>
/// An append-only usage/chargeback record for a single request (architecture.md §7.1). Token fields are
/// optional since not every <see cref="Enums.OperationType"/> reports token usage (e.g. image generation) —
/// <see cref="UsageMetrics"/> carries whatever operation-specific billing signal is available instead.
/// </summary>
public sealed class UsageRecord
{
    public Guid Id { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public string ModelDeploymentName { get; private set; }
    public Guid ConsumerId { get; private set; }
    public string ConsumerName { get; private set; }
    public Guid EndpointId { get; private set; }
    public ApiFamily ApiFamily { get; private set; }
    public OperationType OperationType { get; private set; }
    public string RequestId { get; private set; }
    public string? SessionId { get; private set; }
    public int? PromptTokens { get; private set; }
    public int? CompletionTokens { get; private set; }
    public int? TotalTokens { get; private set; }
    public IReadOnlyDictionary<string, string>? UsageMetrics { get; private set; }
    public bool Streaming { get; private set; }
    public int HttpStatusCode { get; private set; }
    public int DurationMs { get; private set; }

    /// <summary>For EF Core materialization only — domain code must use <see cref="Create"/>.</summary>
    private UsageRecord()
    {
        ModelDeploymentName = null!;
        ConsumerName = null!;
        RequestId = null!;
    }

    private UsageRecord(
        Guid id,
        DateTimeOffset timestamp,
        string modelDeploymentName,
        Guid consumerId,
        string consumerName,
        Guid endpointId,
        ApiFamily apiFamily,
        OperationType operationType,
        string requestId,
        string? sessionId,
        int? promptTokens,
        int? completionTokens,
        int? totalTokens,
        IReadOnlyDictionary<string, string>? usageMetrics,
        bool streaming,
        int httpStatusCode,
        int durationMs)
    {
        Id = id;
        Timestamp = timestamp;
        ModelDeploymentName = modelDeploymentName;
        ConsumerId = consumerId;
        ConsumerName = consumerName;
        EndpointId = endpointId;
        ApiFamily = apiFamily;
        OperationType = operationType;
        RequestId = requestId;
        SessionId = sessionId;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        TotalTokens = totalTokens;
        UsageMetrics = usageMetrics;
        Streaming = streaming;
        HttpStatusCode = httpStatusCode;
        DurationMs = durationMs;
    }

    public static UsageRecord Create(
        Guid id,
        string modelDeploymentName,
        Guid consumerId,
        string consumerName,
        Guid endpointId,
        ApiFamily apiFamily,
        OperationType operationType,
        string requestId,
        bool streaming,
        int httpStatusCode,
        int durationMs,
        string? sessionId = null,
        int? promptTokens = null,
        int? completionTokens = null,
        int? totalTokens = null,
        IReadOnlyDictionary<string, string>? usageMetrics = null,
        DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(modelDeploymentName))
        {
            throw new DomainException("UsageRecord.ModelDeploymentName must not be empty.");
        }

        if (consumerId == Guid.Empty)
        {
            throw new DomainException("UsageRecord.ConsumerId must be a valid Consumer id.");
        }

        if (string.IsNullOrWhiteSpace(consumerName))
        {
            throw new DomainException("UsageRecord.ConsumerName must not be empty.");
        }

        if (endpointId == Guid.Empty)
        {
            throw new DomainException("UsageRecord.EndpointId must be a valid Endpoint id.");
        }

        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new DomainException("UsageRecord.RequestId must not be empty.");
        }

        var hasTokenSignal = promptTokens.HasValue || completionTokens.HasValue || totalTokens.HasValue;
        var hasMetricsSignal = usageMetrics is { Count: > 0 };

        if (!hasTokenSignal && !hasMetricsSignal)
        {
            throw new DomainException(
                "UsageRecord must carry at least one usage signal: token counts or UsageMetrics.");
        }

        return new UsageRecord(
            id,
            timestamp ?? DateTimeOffset.UtcNow,
            modelDeploymentName,
            consumerId,
            consumerName,
            endpointId,
            apiFamily,
            operationType,
            requestId,
            sessionId,
            promptTokens,
            completionTokens,
            totalTokens,
            usageMetrics,
            streaming,
            httpStatusCode,
            durationMs);
    }
}
