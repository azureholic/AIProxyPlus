using System.Text.Json;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Enums;

namespace AIProxyPlus.Infrastructure.Usage;

/// <summary>
/// Handles OpenAI-shaped token usage (chat completions, completions, embeddings) for any OpenAI-compatible
/// <see cref="Provider"/> — the JSON shape (<c>usage.prompt_tokens/completion_tokens/total_tokens</c>) is the
/// same whether the endpoint is OpenAI, Azure OpenAI, or a generic OpenAI-compatible backend.
/// </summary>
public sealed class OpenAiStyleTokenUsageExtractor(Provider provider, OperationType operationType) : IUsageExtractor
{
    public Provider Provider { get; } = provider;

    public OperationType OperationType { get; } = operationType;

    public UsageExtractionResult? ExtractFromResponseBody(string responseBodyJson) => ExtractUsage(responseBodyJson);

    public UsageExtractionResult? TryExtractFromStreamingEvent(string sseEventJson) => ExtractUsage(sseEventJson);

    public bool IsUsageOnlyStreamingEvent(string sseEventJson)
    {
        try
        {
            using var document = JsonDocument.Parse(sseEventJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("usage", out var usageProp) ||
                usageProp.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // OpenAI's forced-usage trailing chunk carries an empty (or absent) `choices` array — every other
            // chunk that legitimately carries content has at least one choice/delta.
            return !root.TryGetProperty("choices", out var choicesProp) ||
                choicesProp.ValueKind != JsonValueKind.Array ||
                choicesProp.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static UsageExtractionResult? ExtractUsage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var promptTokens = TryGetInt(usage, "prompt_tokens");
            var completionTokens = TryGetInt(usage, "completion_tokens");
            var totalTokens = TryGetInt(usage, "total_tokens");

            return promptTokens is null && completionTokens is null && totalTokens is null
                ? null
                : new UsageExtractionResult(promptTokens, completionTokens, totalTokens);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? TryGetInt(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}
