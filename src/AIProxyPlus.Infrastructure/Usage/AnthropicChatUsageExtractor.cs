using System.Text.Json;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Enums;

namespace AIProxyPlus.Infrastructure.Usage;

/// <summary>
/// Handles Anthropic Messages API usage. Non-streaming: <c>usage.input_tokens</c>/<c>output_tokens</c> on the
/// body. Streaming: <c>input_tokens</c> arrives on the <c>message_start</c> event (inside <c>message</c>) and
/// the running <c>output_tokens</c> total arrives on each <c>message_delta</c> event — both are ordinary
/// content-bearing events (never usage-only), so <see cref="IsUsageOnlyStreamingEvent"/> stays false (default).
/// </summary>
public sealed class AnthropicChatUsageExtractor : IUsageExtractor
{
    public Provider Provider => Provider.Anthropic;

    public OperationType OperationType => OperationType.MessagesApi;

    public UsageExtractionResult? ExtractFromResponseBody(string responseBodyJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBodyJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return FromUsageElement(usage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public UsageExtractionResult? TryExtractFromStreamingEvent(string sseEventJson)
    {
        try
        {
            using var document = JsonDocument.Parse(sseEventJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeProp))
            {
                return null;
            }

            var type = typeProp.GetString();
            if (type == "message_start" &&
                root.TryGetProperty("message", out var message) &&
                message.TryGetProperty("usage", out var startUsage))
            {
                return FromUsageElement(startUsage);
            }

            if (type == "message_delta" && root.TryGetProperty("usage", out var deltaUsage))
            {
                return FromUsageElement(deltaUsage);
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UsageExtractionResult? FromUsageElement(JsonElement usage)
    {
        var inputTokens = TryGetInt(usage, "input_tokens");
        var outputTokens = TryGetInt(usage, "output_tokens");

        if (inputTokens is null && outputTokens is null)
        {
            return null;
        }

        var total = inputTokens is not null || outputTokens is not null ? (inputTokens ?? 0) + (outputTokens ?? 0) : (int?)null;
        return new UsageExtractionResult(inputTokens, outputTokens, total);
    }

    private static int? TryGetInt(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}
