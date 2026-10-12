using System.Text.Json;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Enums;

namespace AIProxyPlus.Infrastructure.Usage;

/// <summary>
/// Responses API usage: non-streaming responses carry <c>usage.input_tokens/output_tokens/total_tokens</c> on
/// the body; streaming responses carry the same shape nested under <c>response.usage</c> on the terminal
/// <c>response.completed</c> SSE event (never a usage-only frame — it's the final content-bearing event).
/// </summary>
public sealed class ResponsesUsageExtractor(Provider provider) : IUsageExtractor
{
    public Provider Provider { get; } = provider;

    public OperationType OperationType => OperationType.ResponsesApi;

    public UsageExtractionResult? ExtractFromResponseBody(string responseBodyJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBodyJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("usage", out var usage) &&
                usage.ValueKind == JsonValueKind.Object
                    ? FromUsageElement(usage)
                    : null;
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
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeProp) ||
                typeProp.GetString() != "response.completed" ||
                !root.TryGetProperty("response", out var response) ||
                !response.TryGetProperty("usage", out var usage))
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

    private static UsageExtractionResult? FromUsageElement(JsonElement usage)
    {
        var input = TryGetInt(usage, "input_tokens");
        var output = TryGetInt(usage, "output_tokens");
        var total = TryGetInt(usage, "total_tokens");

        return input is null && output is null && total is null ? null : new UsageExtractionResult(input, output, total);
    }

    private static int? TryGetInt(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}
