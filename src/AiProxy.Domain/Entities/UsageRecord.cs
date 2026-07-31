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
    public Guid Id { get; }
    public DateTimeOffset Timestamp { get; }
    public string ModelDeploymentName { get; }
    public Guid ConsumerId { get; }
    public string ConsumerName { get; }
    public Guid EndpointId { get; }
    public ApiFamily ApiFamily { get; }
    public OperationType OperationType { get; }
    public string RequestId { get; }
    public string? SessionId { get; }
    public int? PromptTokens { get; }
    public int? CompletionTokens { get; }
    public int? TotalTokens { get; }
    public IReadOnlyDictionary<string, string>? UsageMetrics { get; }
    public bool Streaming { get; }
    public int HttpStatusCode { get; }
    public int DurationMs { get; }

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
