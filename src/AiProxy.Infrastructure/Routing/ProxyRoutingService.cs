using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Domain.Services;
using AiProxy.Infrastructure.Observability;
using AiProxy.Infrastructure.Usage;
using Microsoft.AspNetCore.Http;
using Endpoint = AiProxy.Domain.Entities.Endpoint;

namespace AiProxy.Infrastructure.Routing;

/// <summary>
/// Orchestrates model resolution, priority/failover selection, sticky sessions, forwarding, and usage
/// capture for a single ingress request (architecture.md §9/§10). Forwarding itself goes through
/// <see cref="DirectHttpProxy"/>/<see cref="SseStreamRelay"/> rather than YARP's <c>IHttpForwarder</c> — see
/// that type's remarks for why: retries require inspecting an upstream response before committing anything to
/// the client, which <c>IHttpForwarder</c> does not allow.
/// </summary>
public sealed class ProxyRoutingService(
    IModelRepository modelRepository,
    ISessionAffinityStore sessionAffinityStore,
    IEndpointHealthTracker endpointHealthTracker,
    IEndpointRoundRobinCounter roundRobinCounter,
    ICredentialProvider credentialProvider,
    UpstreamRouteResolver upstreamRouteResolver,
    IUsageExtractorResolver usageExtractorResolver,
    UsageRecordChannel usageRecordChannel,
    AiProxyMetrics metrics,
    HttpMessageInvoker httpClient,
    TimeSpan? sessionAffinityTtl = null)
{
    private readonly TimeSpan _sessionAffinityTtl = sessionAffinityTtl ?? TimeSpan.FromMinutes(30);

    public async Task<RoutingFailure?> RouteAsync(
        HttpContext httpContext,
        OperationType operationType,
        CancellationToken cancellationToken,
        Guid? consumerId = null,
        string? consumerName = null)
    {
        using var rootActivity = AiProxyActivitySource.Instance.StartActivity("ai_proxy.request");
        rootActivity?.SetTag("operation_type", operationType.ToString());

        var stopwatch = Stopwatch.StartNew();
        var bodyBytes = await RequestBodyPeek.BufferBodyAsync(httpContext.Request, cancellationToken);
        var fields = operationType == OperationType.SpeechToText
            ? new RequestBodyFields(
                await RequestBodyPeek.ExtractMultipartModelAsync(
                    bodyBytes, httpContext.Request.ContentType, cancellationToken),
                null, false, false, false)
            : RequestBodyPeek.ExtractFields(bodyBytes);

        if (string.IsNullOrWhiteSpace(fields.Model))
        {
            return new RoutingFailure(RoutingFailureReason.ModelNotFound, "Request body must include a 'model' field.");
        }

        Model? model;
        using (AiProxyActivitySource.Instance.StartActivity("model_resolution"))
        {
            model = await modelRepository.GetByNameAsync(fields.Model, cancellationToken);
        }

        if (model is null)
        {
            return new RoutingFailure(RoutingFailureReason.ModelNotFound, $"Model '{fields.Model}' was not found.");
        }

        rootActivity?.SetTag("model", model.Name);

        // Snapshot once per incoming request (not per retry) so a single request's failover attempts move
        // deterministically through the surviving round-robin tier instead of re-rolling on every retry.
        var roundRobinSeed = roundRobinCounter.Next(model.Id);

        // The Responses API is the only operation type with cross-request affinity, and it's automatic — no
        // model-level config flag (architecture.md §9). Everything else round-robins/fails over freely;
        // Realtime API connections (§9) get their own connection-scoped selection, not this per-request path.
        var sessionKey = operationType == OperationType.ResponsesApi ? fields.PreviousResponseId : null;
        SessionAffinityEntry? affinityEntry = null;
        Guid? stickyAssignmentId = null;

        if (sessionKey is not null)
        {
            affinityEntry = await sessionAffinityStore.GetAsync(sessionKey, cancellationToken);
            if (affinityEntry is not null && !affinityEntry.IsExpired())
            {
                stickyAssignmentId = affinityEntry.ModelEndpointId;
            }
        }

        var excludedEndpointIds = new HashSet<Guid>();
        Endpoint? previouslyFailedEndpoint = null;

        // The first endpoint tried this request (typically priority 1) is the one whose error is most
        // meaningful to the caller — captured here so it can be relayed verbatim (status/headers/body,
        // including any 429 Retry-After) if every endpoint ends up failing, instead of a generic 503
        // (architecture.md §9). Only ever set once per request via `??=` below.
        UpstreamFailureSnapshot? firstFailureSnapshot = null;
        while (true)
        {
            var selection = EndpointSelector.SelectEndpoint(model.Endpoints, stickyAssignmentId, excludedEndpointIds, roundRobinSeed);
            if (!selection.IsSuccess)
            {
                var reason = stickyAssignmentId is not null
                    ? RoutingFailureReason.StickyEndpointUnavailable
                    : RoutingFailureReason.NoHealthyEndpoint;
                return new RoutingFailure(reason, selection.FailureReason!, firstFailureSnapshot);
            }

            var assignment = selection.SelectedEndpoint!;
            var endpoint = assignment.Endpoint;

            Uri targetUri;
            try
            {
                targetUri = upstreamRouteResolver.ResolveHttp(assignment, operationType, httpContext.Request.QueryString.Value ?? string.Empty);
            }
            catch (NotSupportedException exception)
            {
                return new RoutingFailure(RoutingFailureReason.NoHealthyEndpoint, exception.Message);
            }

            if (previouslyFailedEndpoint is not null && previouslyFailedEndpoint.Id != endpoint.Id)
            {
                metrics.RecordFailover(model.Name, previouslyFailedEndpoint.BaseUrl, endpoint.BaseUrl);
            }

            EndpointCredential credential;
            using (AiProxyActivitySource.Instance.StartActivity("secret_resolution"))
            {
                credential = await credentialProvider.GetCredentialAsync(assignment, cancellationToken);
            }

            var usageExtractor = TryResolveUsageExtractor(assignment.Provider, operationType);
            var outboundBody = operationType == OperationType.SpeechToText
                ? bodyBytes
                : OutboundBodyMutator.EnsureStreamUsageRequested(bodyBytes, assignment.Provider, operationType, fields.Streaming);

            var outcome = await TryForwardWithRetriesAsync(
                httpContext, endpoint, targetUri, credential, outboundBody, usageExtractor,
                captureResponseId: operationType == OperationType.ResponsesApi,
                clientRequestedUsage: fields.ClientRequestedUsage,
                streaming: fields.Streaming,
                binaryResponse: operationType == OperationType.TextToSpeech,
                cancellationToken);

            if (outcome.Succeeded)
            {
                endpointHealthTracker.MarkHealthy(endpoint.Id);
                metrics.RecordRequest(model.Name, assignment.Provider.ToString(), endpoint.BaseUrl, outcome.StatusCode);
                metrics.RecordDuration(stopwatch.Elapsed.TotalMilliseconds, model.Name, assignment.Provider.ToString(), endpoint.BaseUrl, outcome.StatusCode);
                if (outcome.Usage is not null)
                {
                    metrics.RecordTokens(
                        model.Name, endpoint.BaseUrl, outcome.Usage.PromptTokens, outcome.Usage.CompletionTokens, outcome.Usage.TotalTokens);
                }

                await PersistResponsesApiAffinityAsync(
                    model, assignment, operationType, fields.StoreFalse, outcome.CapturedResponseId, cancellationToken);

                // ConsumerId is only populated once ingress authentication (Phase 7) resolves a caller; until
                // then there is no valid Consumer to attribute usage to, so capture is skipped rather than
                // violating the UsageRecord.ConsumerId invariant with a placeholder id. Likewise, UsageRecord
                // requires at least one usage signal, so a response the extractor couldn't read any usage from
                // (e.g. no `usage` field at all) is not recorded rather than persisting an empty row.
                if (consumerId is not null && outcome.Usage is not null)
                {
                    EnqueueUsageRecord(
                        model, assignment, operationType, fields, sessionKey, consumerId.Value, consumerName, outcome, stopwatch.ElapsedMilliseconds);
                }

                return null;
            }

            endpointHealthTracker.MarkUnhealthy(endpoint.Id);
            previouslyFailedEndpoint = endpoint;
            firstFailureSnapshot ??= outcome.Failure;

            if (stickyAssignmentId is not null)
            {
                // Deliberately not attaching a snapshot here: a stateful session must fail fast with a clear,
                // generic error rather than have a stale/misleading upstream body replayed to the client — see
                // architecture.md §9.
                return new RoutingFailure(
                    RoutingFailureReason.StickyEndpointUnavailable,
                    $"Sticky assignment '{stickyAssignmentId}' failed and failover is disallowed for stateful sessions.");
            }

            excludedEndpointIds.Add(endpoint.Id);
        }
    }

    private IUsageExtractor? TryResolveUsageExtractor(Provider provider, OperationType operationType)
    {
        try
        {
            return usageExtractorResolver.Resolve(provider, operationType);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    private void EnqueueUsageRecord(
        Model model,
        ModelEndpoint assignment,
        OperationType operationType,
        RequestBodyFields fields,
        string? sessionKey,
        Guid consumerId,
        string? consumerName,
        ForwardOutcome outcome,
        long durationMs)
    {
        var usageRecord = UsageRecord.Create(
            Guid.NewGuid(),
            model.Name,
            consumerId,
            consumerName ?? "unknown",
            assignment.EndpointId,
            assignment.Name,
            assignment.Provider,
            operationType,
            requestId: Guid.NewGuid().ToString("n"),
            streaming: fields.Streaming,
            httpStatusCode: outcome.StatusCode,
            durationMs: (int)Math.Min(durationMs, int.MaxValue),
            sessionId: sessionKey,
            promptTokens: outcome.Usage?.PromptTokens,
            completionTokens: outcome.Usage?.CompletionTokens,
            totalTokens: outcome.Usage?.TotalTokens,
            usageMetrics: outcome.Usage?.UsageMetrics);

        usageRecordChannel.Enqueue(usageRecord);
    }

    private async Task<ForwardOutcome> TryForwardWithRetriesAsync(
        HttpContext httpContext,
        Endpoint endpoint,
        Uri targetUri,
        EndpointCredential credential,
        byte[] bodyBytes,
        IUsageExtractor? usageExtractor,
        bool captureResponseId,
        bool clientRequestedUsage,
        bool streaming,
        bool binaryResponse,
        CancellationToken cancellationToken)
    {
        UpstreamFailureSnapshot? lastFailureSnapshot = null;
        for (var attempt = 0; attempt <= endpoint.MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(JitteredBackoff(attempt), cancellationToken);
            }

            HttpResponseMessage? response;
            using var backendCallActivity = AiProxyActivitySource.Instance.StartActivity("backend_call");
            backendCallActivity?.SetTag("endpoint", endpoint.BaseUrl);
            backendCallActivity?.SetTag("attempt", attempt);
            try
            {
                using var request = DirectHttpProxy.BuildRequest(httpContext, targetUri, bodyBytes, credential);
                response = await httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException)
            {
                // Connection-level failure (refused, DNS, TLS, etc.) — treat as retriable and try the next attempt.
                continue;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Upstream timeout distinct from the caller cancelling the original request — also retriable.
                continue;
            }

            using (response)
            {
                var statusCode = (int)response.StatusCode;
                backendCallActivity?.SetTag("status_code", statusCode);
                if (statusCode == 429 || statusCode >= 500)
                {
                    lastFailureSnapshot = await DirectHttpProxy.CaptureFailureSnapshotAsync(response, cancellationToken);
                    continue;
                }

                if (streaming)
                {
                    var relayResult = await SseStreamRelay.RelayAsync(
                        response,
                        httpContext,
                        usageExtractor,
                        clientRequestedUsage,
                        captureResponseId ? TryExtractResponseIdFromCompletedEvent : null,
                        cancellationToken);
                    return new ForwardOutcome(true, statusCode, relayResult.Usage, relayResult.CapturedResponseId);
                }

                if (binaryResponse)
                {
                    await DirectHttpProxy.CopyResponseAsync(response, httpContext, cancellationToken);
                    return new ForwardOutcome(true, statusCode, null, null);
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                UsageExtractionResult? usage;
                using (AiProxyActivitySource.Instance.StartActivity("usage_extraction"))
                {
                    usage = usageExtractor?.ExtractFromResponseBody(responseBody);
                }

                var capturedResponseId = captureResponseId ? TryExtractResponseId(responseBody) : null;
                await DirectHttpProxy.CopyBufferedResponseAsync(response, responseBody, httpContext, cancellationToken);
                return new ForwardOutcome(true, statusCode, usage, capturedResponseId);
            }
        }

        return new ForwardOutcome(false, lastFailureSnapshot?.StatusCode ?? 0, null, null, lastFailureSnapshot);
    }

    private async Task PersistResponsesApiAffinityAsync(
        Model model,
        ModelEndpoint assignment,
        OperationType operationType,
        bool storeFalse,
        string? capturedResponseId,
        CancellationToken cancellationToken)
    {
        if (operationType != OperationType.ResponsesApi || storeFalse || capturedResponseId is null)
        {
            return;
        }

        var entry = SessionAffinityEntry.Create(
            capturedResponseId, model.Id, assignment.Id, assignment.EndpointId, _sessionAffinityTtl);
        await sessionAffinityStore.SaveAsync(entry, cancellationToken);
    }

    private static string? TryExtractResponseId(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("id", out var idProp) &&
                idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryExtractResponseIdFromCompletedEvent(string sseEventJson)
    {
        try
        {
            using var document = JsonDocument.Parse(sseEventJson);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("type", out var typeProp) &&
                typeProp.GetString() == "response.completed" &&
                root.TryGetProperty("response", out var responseProp) &&
                responseProp.TryGetProperty("id", out var idProp) &&
                idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TimeSpan JitteredBackoff(int attempt)
    {
        var baseDelayMs = Math.Min(100 * Math.Pow(2, attempt - 1), 2000);
        var jitterMs = Random.Shared.Next(0, 50);
        return TimeSpan.FromMilliseconds(baseDelayMs + jitterMs);
    }

    private readonly record struct ForwardOutcome(
        bool Succeeded, int StatusCode, UsageExtractionResult? Usage, string? CapturedResponseId, UpstreamFailureSnapshot? Failure = null);
}
