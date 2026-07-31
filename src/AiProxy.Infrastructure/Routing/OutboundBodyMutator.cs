using System.Text.Json;
using System.Text.Json.Nodes;
using AiProxy.Domain.Enums;

namespace AiProxy.Infrastructure.Routing;

/// <summary>
/// Mutates the outbound request body to force usage reporting on streaming requests (architecture.md §8/§10),
/// regardless of whether the client itself asked for it — the streaming interceptor then suppresses the
/// resulting usage-only frame from the client-facing stream if the client didn't request it.
/// </summary>
public static class OutboundBodyMutator
{
    private static readonly HashSet<ApiFamily> OpenAiStyleFamilies =
        [ApiFamily.OpenAI, ApiFamily.AzureOpenAI, ApiFamily.OpenAICompatible];

    /// <summary>Returns a body with <c>stream_options.include_usage: true</c> injected, or the original bytes if unchanged.</summary>
    public static byte[] EnsureStreamUsageRequested(byte[] bodyBytes, ApiFamily apiFamily, OperationType operationType, bool streaming)
    {
        if (!streaming || !OpenAiStyleFamilies.Contains(apiFamily) ||
            operationType is not (OperationType.ChatCompletion or OperationType.Completion))
        {
            return bodyBytes;
        }

        try
        {
            var node = JsonNode.Parse(bodyBytes)?.AsObject();
            if (node is null)
            {
                return bodyBytes;
            }

            var streamOptions = node["stream_options"] as JsonObject ?? new JsonObject();
            streamOptions["include_usage"] = true;
            node["stream_options"] = streamOptions;

            return JsonSerializer.SerializeToUtf8Bytes(node);
        }
        catch (JsonException)
        {
            return bodyBytes;
        }
    }
}
