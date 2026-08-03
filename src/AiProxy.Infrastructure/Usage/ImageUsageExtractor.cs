using System.Text.Json;
using AiProxy.Application.Abstractions;
using AiProxy.Domain.Enums;

namespace AiProxy.Infrastructure.Usage;

/// <summary>
/// Images API responses carry no token counts — the billing signal is image count/size/quality, captured into
/// <see cref="UsageExtractionResult.UsageMetrics"/> instead of the token fields. Images API is never streamed.
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
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

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

            return metrics.Count == 0 ? null : new UsageExtractionResult(UsageMetrics: metrics);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public UsageExtractionResult? TryExtractFromStreamingEvent(string sseEventJson) => null;
}
