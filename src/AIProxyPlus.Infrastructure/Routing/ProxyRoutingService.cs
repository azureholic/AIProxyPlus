using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Domain.Services;
using AIProxyPlus.Infrastructure.Observability;
using AIProxyPlus.Infrastructure.Usage;
using Microsoft.AspNetCore.Http;
using Endpoint = AIProxyPlus.Domain.Entities.Endpoint;

namespace AIProxyPlus.Infrastructure.Routing;

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
    AIProxyPlusMetrics metrics,
    HttpMessageInvoker httpClient,
    TimeSpan? sessionAffinityTtl = null)
{
    private readonly TimeSpan _sessionAffinityTtl = sessionAffinityTtl ?? TimeSpan.FromMinutes(30);

    public async Task<RoutingFailure?> RouteAsync(
        HttpContext httpContext,
        OperationType operationType,
        CancellationToken cancellationToken,
        Guid? consumerId = null,
        string? consumerName = null,
        IReadOnlySet<Guid>? allowedModelIds = null)
    {
        using var rootActivity = AIProxyPlusActivitySource.Instance.StartActivity("aiproxyplus.request");
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
        using (AIProxyPlusActivitySource.Instance.StartActivity("model_resolution"))
        {
            model = await modelRepository.GetByNameAsync(fields.Model, cancellationToken);
        }

        if (model is null)
        {
            return new RoutingFailure(RoutingFailureReason.ModelNotFound, $"Model '{fields.Model}' was not found.");
        }

        if (allowedModelIds is not null && !allowedModelIds.Contains(model.Id))
        {
            return new RoutingFailure(
                RoutingFailureReason.ModelAccessDenied,
                $"Consumer '{consumerName}' is not allowed to use model '{model.Name}'.");
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
        if (stickyAssignmentId is null)
        {
            // Cross-request passive circuit breaker (architecture.md §9): an endpoint that failed recently
            // enough to still be within the tracker's cooldown window is skipped on this new request too,
            // instead of being retried from a cold start every single time.
            foreach (var candidate in model.Endpoints)
            {
                if (endpointHealthTracker.GetHealth(candidate.EndpointId) == EndpointHealthState.Unhealthy)
                {
                    excludedEndpointIds.Add(candidate.EndpointId);
                }
            }
        }

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

            EndpointCredentialCandidates credentials;
            using (AIProxyPlusActivitySource.Instance.StartActivity("secret_resolution"))
            {
                credentials = await credentialProvider.GetCredentialCandidatesAsync(assignment, cancellationToken);
            }

            var usageExtractor = TryResolveUsageExtractor(assignment.Provider, operationType);
            var outboundBody = operationType == OperationType.SpeechToText
                ? bodyBytes
                : OutboundBodyMutator.EnsureStreamUsageRequested(bodyBytes, assignment.Provider, operationType, fields.Streaming);

            var outcome = await TryForwardWithRetriesAsync(
                httpContext, endpoint, assignment.Provider, targetUri, credentials, outboundBody, usageExtractor,
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

            if (outcome.MayHaveBeenProcessed)
            {
                // Report this endpoint's own outcome (or a generic error when there was no response), never an
                // earlier endpoint's replay-safe snapshot: a stale 429/Retry-After would invite a confident retry of
                // a request this endpoint may already have processed.
                return new RoutingFailure(
                    RoutingFailureReason.NoHealthyEndpoint,
                    outcome.StatusCode > 0
                        ? $"The endpoint returned {outcome.StatusCode}, so the request may have been processed and was not retried or failed over."
                        : "The endpoint failed after the request may have been sent, so it was not retried or failed over.",
                    outcome.Failure);
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
        Provider provider,
        Uri targetUri,
        EndpointCredentialCandidates credentials,
        byte[] bodyBytes,
        IUsageExtractor? usageExtractor,
        bool captureResponseId,
        bool clientRequestedUsage,
        bool streaming,
        bool binaryResponse,
        CancellationToken cancellationToken)
    {
        UpstreamFailureSnapshot? lastFailureSnapshot = null;
        UpstreamFailureSnapshot? activeCredentialRejection = null;
        var credential = credentials.Active;
        var fallbackAttempted = false;
        var skipBackoff = false;
        for (var attempt = 0; attempt <= endpoint.MaxRetries; attempt++)
        {
            if (attempt > 0 && !skipBackoff)
            {
                await Task.Delay(JitteredBackoff(attempt), cancellationToken);
            }
            skipBackoff = false;

            HttpResponseMessage? response;
            using var backendCallActivity = AIProxyPlusActivitySource.Instance.StartActivity("backend_call");
            backendCallActivity?.SetTag("endpoint", endpoint.BaseUrl);
            backendCallActivity?.SetTag("attempt", attempt);

            // Endpoint.TimeoutSeconds bounds the wait for response headers. It stops counting once they arrive so a
            // long streamed response is never cut off.
            using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            responseTimeout.CancelAfter(TimeSpan.FromSeconds(endpoint.TimeoutSeconds));
            try
            {
                using var request = DirectHttpProxy.BuildRequest(httpContext, targetUri, bodyBytes, credential);
                response = await httpClient.SendAsync(request, responseTimeout.Token);
                responseTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            }
            catch (HttpRequestException exception)
            {
                if (UpstreamFailureClassifier.FailedBeforeRequestWasSent(exception))
                {
                    if (fallbackAttempted && activeCredentialRejection is not null)
                    {
                        // The fallback request never reached the upstream, so the active-key rejection is the real outcome.
                        metrics.RecordCredentialFallback(provider.ToString(), endpoint.Id, endpoint.Name, "failed");
                        await DirectHttpProxy.ReplaySnapshotAsync(activeCredentialRejection, httpContext, cancellationToken);
                        return new ForwardOutcome(true, activeCredentialRejection.StatusCode, null, null);
                    }

                    // Refused, DNS, or TLS failure: nothing reached the upstream, so trying again is safe.
                    continue;
                }

                // The connection was established, so the upstream may have received and acted on the request. This
                // includes a fallback attempt: replaying the earlier 401 would hide that and invite a duplicate.
                if (fallbackAttempted)
                {
                    metrics.RecordCredentialFallback(provider.ToString(), endpoint.Id, endpoint.Name, "failed");
                }

                return new ForwardOutcome(false, 0, null, null, MayHaveBeenProcessed: true);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Our own response timeout firing is ambiguous: the request was sent and may have been processed. Only
                // the handler's connect timeout (a TimeoutException cause) proves nothing was sent.
                if (!responseTimeout.IsCancellationRequested && UpstreamFailureClassifier.FailedBeforeRequestWasSent(exception))
                {
                    if (fallbackAttempted && activeCredentialRejection is not null)
                    {
                        metrics.RecordCredentialFallback(provider.ToString(), endpoint.Id, endpoint.Name, "failed");
                        await DirectHttpProxy.ReplaySnapshotAsync(activeCredentialRejection, httpContext, cancellationToken);
                        return new ForwardOutcome(true, activeCredentialRejection.StatusCode, null, null);
                    }

                    // The connect timeout fired before anything was sent, so trying again is safe.
                    continue;
                }

                if (fallbackAttempted)
                {
                    metrics.RecordCredentialFallback(provider.ToString(), endpoint.Id, endpoint.Name, "failed");
                }

                return new ForwardOutcome(false, 0, null, null, MayHaveBeenProcessed: true);
            }

            using (response)
            {
                var statusCode = (int)response.StatusCode;
                backendCallActivity?.SetTag("status_code", statusCode);
                if (statusCode == StatusCodes.Status401Unauthorized
                    && credentials.ResolveFallbackAsync is not null
                    && !fallbackAttempted)
                {
                    activeCredentialRejection = await DirectHttpProxy.CaptureFailureSnapshotAsync(response, cancellationToken);
                    fallbackAttempted = true;
                    metrics.RecordCredentialFallback(provider.ToString(), endpoint.Id, endpoint.Name, "attempted");
                    try
                    {
                        credential = await credentials.ResolveFallbackAsync(cancellationToken);
                    }
                    catch
                    {
                        metrics.RecordCredentialFallback(provider.ToString(), endpoint.Id, endpoint.Name, "failed");
                        await DirectHttpProxy.ReplaySnapshotAsync(activeCredentialRejection, httpContext, cancellationToken);
                        return new ForwardOutcome(true, activeCredentialRejection.StatusCode, null, null);
                    }
                    skipBackoff = true;
                    attempt--;
                    continue;
                }

                // A 5xx other than the replay-safe ones may mean the upstream processed the request. After a fallback
                // attempt it must not be masked by replaying the earlier 401, so it skips the rejection replay below.
                var mayHaveBeenProcessed = statusCode >= 500 && !UpstreamFailureClassifier.IsSafeToReplay(provider, statusCode);
                if (fallbackAttempted && activeCredentialRejection is not null
                    && statusCode >= 400 && !mayHaveBeenProcessed)
                {
                    metrics.RecordCredentialFallback(provider.ToString(), endpoint.Id, endpoint.Name, "failed");
                    await DirectHttpProxy.ReplaySnapshotAsync(activeCredentialRejection, httpContext, cancellationToken);
                    return new ForwardOutcome(true, activeCredentialRejection.StatusCode, null, null);
                }

                if (fallbackAttempted)
                {
                    metrics.RecordCredentialFallback(
                        provider.ToString(), endpoint.Id, endpoint.Name, statusCode < 400 ? "succeeded" : "failed");
                }
                if (UpstreamFailureClassifier.IsSafeToReplay(provider, statusCode))
                {
                    lastFailureSnapshot = await DirectHttpProxy.CaptureFailureSnapshotAsync(response, cancellationToken);
                    continue;
                }

                if (mayHaveBeenProcessed)
                {
                    // The upstream may already have processed the request (state created, tokens billed), so a
                    // replay here, to this endpoint or another, could duplicate the operation. Stop and let the
                    // caller see the upstream's own error.
                    UpstreamFailureSnapshot ambiguousFailure;
                    try
                    {
                        ambiguousFailure = await DirectHttpProxy.CaptureFailureSnapshotAsync(response, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // The caller hung up while the error body was being read. The endpoint's 5xx is already
                        // known, so it must still be recorded as failed rather than lost to the cancellation.
                        ambiguousFailure = new UpstreamFailureSnapshot(
                            statusCode, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase), string.Empty);
                    }

                    return new ForwardOutcome(false, statusCode, null, null, ambiguousFailure, MayHaveBeenProcessed: true);
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
                using (AIProxyPlusActivitySource.Instance.StartActivity("usage_extraction"))
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
        bool Succeeded,
        int StatusCode,
        UsageExtractionResult? Usage,
        string? CapturedResponseId,
        UpstreamFailureSnapshot? Failure = null,
        bool MayHaveBeenProcessed = false);
}
