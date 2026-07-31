using System.Text;
using AiProxy.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace AiProxy.Infrastructure.Routing;

/// <summary>Merged usage (if any) observed across the relayed SSE stream, plus a captured Responses API id, if any.</summary>
internal sealed record SseRelayResult(UsageExtractionResult? Usage, string? CapturedResponseId);

/// <summary>
/// Streams an SSE (Server-Sent Events) response from upstream to the client incrementally — no full-body
/// buffering, so time-to-first-byte is unaffected — while extracting the usage signal from whichever frame(s)
/// carry it and, when the client itself never requested usage, suppressing OpenAI's forced trailing
/// usage-only frame from the client-facing stream (architecture.md §8/§10).
/// </summary>
internal static class SseStreamRelay
{
    public static async Task<SseRelayResult> RelayAsync(
        HttpResponseMessage response,
        HttpContext httpContext,
        IUsageExtractor? extractor,
        bool clientRequestedUsage,
        Func<string, string?>? tryExtractResponseId,
        CancellationToken cancellationToken)
    {
        DirectHttpProxy.CopyStatusAndHeaders(response, httpContext);
        httpContext.Response.Headers.Remove("Content-Length");

        await using var upstream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(upstream);
        var writer = httpContext.Response.Body;

        UsageExtractionResult? mergedUsage = null;
        string? capturedResponseId = null;
        var block = new List<string>();

        async Task FlushBlockAsync()
        {
            if (block.Count == 0)
            {
                return;
            }

            var dataPayload = string.Join(
                '\n',
                block.Where(l => l.StartsWith("data:", StringComparison.Ordinal))
                    .Select(l => l["data:".Length..].TrimStart()));

            var isUsageOnly = false;
            if (!string.IsNullOrEmpty(dataPayload))
            {
                if (extractor is not null)
                {
                    var usage = extractor.TryExtractFromStreamingEvent(dataPayload);
                    if (usage is not null)
                    {
                        mergedUsage = Merge(mergedUsage, usage);
                        isUsageOnly = extractor.IsUsageOnlyStreamingEvent(dataPayload);
                    }
                }

                capturedResponseId ??= tryExtractResponseId?.Invoke(dataPayload);
            }

            if (isUsageOnly && !clientRequestedUsage)
            {
                block.Clear();
                return;
            }

            foreach (var line in block)
            {
                await writer.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), cancellationToken);
            }

            await writer.WriteAsync(Encoding.UTF8.GetBytes("\n"), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            block.Clear();
        }

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                await FlushBlockAsync();
                continue;
            }

            block.Add(line);
        }

        await FlushBlockAsync();

        return new SseRelayResult(mergedUsage, capturedResponseId);
    }

    private static UsageExtractionResult Merge(UsageExtractionResult? existing, UsageExtractionResult next)
    {
        if (existing is null)
        {
            return next;
        }

        Dictionary<string, string>? metrics = null;
        if (existing.UsageMetrics is not null || next.UsageMetrics is not null)
        {
            metrics = new Dictionary<string, string>(existing.UsageMetrics ?? new Dictionary<string, string>());
            foreach (var (key, value) in next.UsageMetrics ?? new Dictionary<string, string>())
            {
                metrics[key] = value;
            }
        }

        return new UsageExtractionResult(
            next.PromptTokens ?? existing.PromptTokens,
            next.CompletionTokens ?? existing.CompletionTokens,
            next.TotalTokens ?? existing.TotalTokens,
            metrics);
    }
}
