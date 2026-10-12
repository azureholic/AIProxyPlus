using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace AIProxyPlus.Infrastructure.Routing;

/// <summary>Fields the router needs from the request body without fully parsing/understanding the payload (§9).</summary>
public sealed record RequestBodyFields(
    string? Model,
    string? PreviousResponseId,
    bool StoreFalse,
    bool ClientRequestedUsage,
    bool Streaming);

public static class RequestBodyPeek
{
    /// <summary>Buffers the request body so it can be read here and still forwarded downstream unmodified.</summary>
    public static async Task<byte[]> BufferBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        await using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        request.Body.Position = 0;
        return buffer.ToArray();
    }

    /// <summary>
    /// Extracts routing-relevant fields from a JSON body. Works for OpenAI, Anthropic, Images, and Responses
    /// API payloads since they all carry a top-level "model" field (architecture.md §9).
    /// </summary>
    public static RequestBodyFields ExtractFields(byte[] jsonBody)
    {
        if (jsonBody.Length == 0)
        {
            return new RequestBodyFields(null, null, false, false, false);
        }

        try
        {
            using var document = JsonDocument.Parse(jsonBody.AsMemory());
            var root = document.RootElement;

            var model = TryGetString(root, "model");
            var previousResponseId = TryGetString(root, "previous_response_id");
            var storeFalse = root.TryGetProperty("store", out var storeProp) && storeProp.ValueKind == JsonValueKind.False;
            var streaming = root.TryGetProperty("stream", out var streamProp) && streamProp.ValueKind == JsonValueKind.True;
            var clientRequestedUsage =
                root.TryGetProperty("stream_options", out var streamOptions) &&
                streamOptions.ValueKind == JsonValueKind.Object &&
                streamOptions.TryGetProperty("include_usage", out var includeUsage) &&
                includeUsage.ValueKind == JsonValueKind.True;

            return new RequestBodyFields(model, previousResponseId, storeFalse, clientRequestedUsage, streaming);
        }
        catch (JsonException)
        {
            return new RequestBodyFields(null, null, false, false, false);
        }
    }

    public static async Task<string?> ExtractMultipartModelAsync(
        byte[] body,
        string? contentType,
        CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ||
            !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary))
        {
            return null;
        }

        var reader = new MultipartReader(boundary, new MemoryStream(body, writable: false));
        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(cancellationToken)) is not null)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(HeaderUtilities.RemoveQuotes(disposition.Name).Value, "model", StringComparison.Ordinal))
            {
                continue;
            }

            using var modelReader = new StreamReader(section.Body);
            return (await modelReader.ReadToEndAsync(cancellationToken)).Trim();
        }

        return null;
    }

    private static string? TryGetString(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
