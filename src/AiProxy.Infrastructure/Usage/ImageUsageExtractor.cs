using System.Text.Json;
using AiProxy.Application.Abstractions;
using AiProxy.Domain.Enums;

namespace AiProxy.Infrastructure.Usage;

/// <summary>
/// Images API responses carry image count/size/quality as the billing signal, captured into
/// <see cref="UsageExtractionResult.UsageMetrics"/>. GPT Image-style models additionally report token usage
/// (<c>usage.input_tokens/output_tokens/total_tokens</c>) alongside those metrics, on both the non-streaming
/// body and the terminal <c>image_generation.completed</c>/<c>image_edit.completed</c> SSE event. Microsoft's
/// own MAI-Image-style models report usage in a differently-shaped object instead
/// (<c>usage.num_input_text_tokens</c>/<c>num_input_image_tokens</c>/<c>num_output_tokens</c>), which is mapped
/// onto the same <see cref="UsageExtractionResult.PromptTokens"/>/<see cref="UsageExtractionResult.CompletionTokens"/>
/// fields. Legacy/deprecated image models and <c>/images/variations</c> never report token usage.
/// </summary>
public sealed class ImageUsageExtractor(Provider provider, OperationType operationType) : IUsageExtractor
{
    public Provider Provider { get; } = provider;

    public OperationType OperationType { get; } = operationType;

    public UsageExtractionResult? ExtractFromResponseBody(string responseBodyJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBodyJson);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object ? FromResponseRoot(root) : null;
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
                typeProp.GetString() is not ("image_generation.completed" or "image_edit.completed"))
            {
                return null;
            }

            return FromResponseRoot(root);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UsageExtractionResult? FromResponseRoot(JsonElement root)
    {
        var metrics = new Dictionary<string, string>();

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            metrics["image_count"] = data.GetArrayLength().ToString();

            if (data.GetArrayLength() > 0 &&
                data[0].ValueKind == JsonValueKind.Object &&
                data[0].TryGetProperty("size", out var sizeProp) &&
                sizeProp.ValueKind == JsonValueKind.String)
            {
                metrics["size"] = sizeProp.GetString()!;
            }
        }

        if (root.TryGetProperty("size", out var topLevelSize) && topLevelSize.ValueKind == JsonValueKind.String)
        {
            metrics.TryAdd("size", topLevelSize.GetString()!);
        }

        if (root.TryGetProperty("quality", out var qualityProp) && qualityProp.ValueKind == JsonValueKind.String)
        {
            metrics["quality"] = qualityProp.GetString()!;
        }

        int? promptTokens = null;
        int? completionTokens = null;
        int? totalTokens = null;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            promptTokens = TryGetInt(usage, "input_tokens");
            completionTokens = TryGetInt(usage, "output_tokens");
            totalTokens = TryGetInt(usage, "total_tokens");

            if (promptTokens is null && completionTokens is null && totalTokens is null)
            {
                (promptTokens, completionTokens, totalTokens) = FromMicrosoftAiUsageShape(usage);
            }
        }

        var hasTokens = promptTokens is not null || completionTokens is not null || totalTokens is not null;
        if (metrics.Count == 0 && !hasTokens)
        {
            return null;
        }

        return new UsageExtractionResult(promptTokens, completionTokens, totalTokens, metrics.Count == 0 ? null : metrics);
    }

    // MAI-Image-style models report usage.num_input_text_tokens/num_input_image_tokens/num_output_tokens
    // instead of OpenAI's input_tokens/output_tokens/total_tokens shape.
    private static (int? PromptTokens, int? CompletionTokens, int? TotalTokens) FromMicrosoftAiUsageShape(JsonElement usage)
    {
        var inputText = TryGetInt(usage, "num_input_text_tokens");
        var inputImage = TryGetInt(usage, "num_input_image_tokens");
        var output = TryGetInt(usage, "num_output_tokens");

        if (inputText is null && inputImage is null && output is null)
        {
            return (null, null, null);
        }

        var promptTokens = inputText is null && inputImage is null ? (int?)null : (inputText ?? 0) + (inputImage ?? 0);
        var totalTokens = (promptTokens ?? 0) + (output ?? 0);
        return (promptTokens, output, totalTokens);
    }

    private static int? TryGetInt(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}
