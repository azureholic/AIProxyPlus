using AiProxy.Domain.Enums;

namespace AiProxy.Application.Abstractions;

/// <summary>
/// Extracts token counts and/or <see cref="UsageExtractionResult.UsageMetrics"/> from a request/response for one
/// (<see cref="ApiFamily"/>, <see cref="OperationType"/>) pair — architecture.md §4/§10. Real JSON/SSE parsing is
/// implemented in Infrastructure (Phase 6); this port only defines the shape.
/// </summary>
public interface IUsageExtractor
{
    ApiFamily ApiFamily { get; }

    OperationType OperationType { get; }

    /// <summary>Extracts usage from a complete, non-streaming JSON response body.</summary>
    UsageExtractionResult? ExtractFromResponseBody(string responseBodyJson);

    /// <summary>Extracts usage from a single SSE event, if that event carries the usage signal; otherwise null.</summary>
    UsageExtractionResult? TryExtractFromStreamingEvent(string sseEventJson);
}
