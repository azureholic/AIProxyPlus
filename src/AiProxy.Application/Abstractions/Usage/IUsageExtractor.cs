using AiProxy.Domain.Enums;

namespace AiProxy.Application.Abstractions;

/// <summary>
/// Extracts token counts and/or <see cref="UsageExtractionResult.UsageMetrics"/> from a request/response for one
/// (<see cref="Provider"/>, <see cref="OperationType"/>) pair — architecture.md §4/§10. Real JSON/SSE parsing is
/// implemented in Infrastructure (Phase 6); this port only defines the shape.
/// </summary>
public interface IUsageExtractor
{
    Provider Provider { get; }

    OperationType OperationType { get; }

    /// <summary>Extracts usage from a complete, non-streaming JSON response body.</summary>
    UsageExtractionResult? ExtractFromResponseBody(string responseBodyJson);

    /// <summary>Extracts usage from a single SSE event, if that event carries the usage signal; otherwise null.</summary>
    UsageExtractionResult? TryExtractFromStreamingEvent(string sseEventJson);

    /// <summary>
    /// True when the given SSE event exists *only* to carry the usage signal we forced onto the outbound
    /// request (e.g. OpenAI's trailing chunk with an empty <c>choices</c> array when
    /// <c>stream_options.include_usage</c> was injected) and should be suppressed from the client-facing
    /// stream when the client itself never asked for usage. Families whose usage frames are otherwise
    /// necessary content (e.g. Anthropic's <c>message_start</c>/<c>message_delta</c>) should never suppress.
    /// </summary>
    bool IsUsageOnlyStreamingEvent(string sseEventJson) => false;
}
