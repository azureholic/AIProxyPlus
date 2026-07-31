namespace AiProxy.Application.Abstractions;

/// <summary>The usage/billing signal pulled out of a response by an <see cref="IUsageExtractor"/>.</summary>
public sealed record UsageExtractionResult(
    int? PromptTokens = null,
    int? CompletionTokens = null,
    int? TotalTokens = null,
    IReadOnlyDictionary<string, string>? UsageMetrics = null);
