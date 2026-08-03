using System.Diagnostics;
using System.Net.Http;
using System.Text;
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
/// Orchestrates deployment resolution, priority/failover selection, sticky sessions, forwarding, and usage
/// capture for a single ingress request (architecture.md §9/§10). Forwarding itself goes through
/// <see cref="DirectHttpProxy"/>/<see cref="SseStreamRelay"/> rather than YARP's <c>IHttpForwarder</c> — see
/// that type's remarks for why: retries require inspecting an upstream response before committing anything to
/// the client, which <c>IHttpForwarder</c> does not allow.
/// </summary>
public sealed class ProxyRoutingService(
    IModelDeploymentRepository modelDeploymentRepository,
    ISessionAffinityStore sessionAffinityStore,
    IEndpointHealthTracker endpointHealthTracker,
    IEndpointRoundRobinCounter roundRobinCounter,
    ICredentialProvider credentialProvider,
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
        var body = await RequestBodyPeek.BufferBodyAsync(httpContext.Request, cancellationToken);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var fields = RequestBodyPeek.ExtractFields(body);

        if (string.IsNullOrWhiteSpace(fields.Model))
        {
            return new RoutingFailure(RoutingFailureReason.DeploymentNotFound, "Request body must include a 'model' field.");
        }

        ModelDeployment? deployment;
        using (AiProxyActivitySource.Instance.StartActivity("deployment_resolution"))
        {
            deployment = await modelDeploymentRepository.GetByNameAsync(fields.Model, cancellationToken);
        }

        if (deployment is null)
        {
            return new RoutingFailure(RoutingFailureReason.DeploymentNotFound, $"Model deployment '{fields.Model}' was not found.");
        }

        rootActivity?.SetTag("deployment", deployment.Name);

        // Snapshot once per incoming request (not per retry) so a single request's failover attempts move
        // deterministically through the surviving round-robin tier instead of re-rolling on every retry.
        var roundRobinSeed = roundRobinCounter.Next(deployment.Id);

        // The Responses API is the only operation type with cross-request affinity, and it's automatic — no
        // deployment-level config flag (architecture.md §9). Everything else round-robins/fails over freely;
        // Realtime API connections (§9) get their own connection-scoped selection, not this per-request path.
        var sessionKey = operationType == OperationType.ResponsesApi ? fields.PreviousResponseId : null;
        SessionAffinityEntry? affinityEntry = null;
        Guid? stickyEndpointId = null;

        if (sessionKey is not null)
        {
            affinityEntry = await sessionAffinityStore.GetAsync(sessionKey, cancellationToken);
            if (affinityEntry is not null && !affinityEntry.IsExpired())
            {
                stickyEndpointId = affinityEntry.EndpointId;
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
            var selection = EndpointSelector.SelectEndpoint(deployment.Endpoints, stickyEndpointId, excludedEndpointIds, roundRobinSeed);
            if (!selection.IsSuccess)
            {
                var reason = stickyEndpointId is not null
                    ? RoutingFailureReason.StickyEndpointUnavailable
                    : RoutingFailureReason.NoHealthyEndpoint;
                return new RoutingFailure(reason, selection.FailureReason!, firstFailureSnapshot);
            }

            var assignment = selection.SelectedEndpoint!;
            var endpoint = assignment.Endpoint;

            if (previouslyFailedEndpoint is not null && previouslyFailedEndpoint.Id != endpoint.Id)
            {
                metrics.RecordFailover(deployment.Name, previouslyFailedEndpoint.BaseUrl, endpoint.BaseUrl);
            }

            EndpointCredential credential;
            using (AiProxyActivitySource.Instance.StartActivity("secret_resolution"))
            {
                credential = await credentialProvider.GetCredentialAsync(endpoint, cancellationToken);
            }

            var usageExtractor = TryResolveUsageExtractor(endpoint.ApiFamily, operationType);
            var outboundBody = OutboundBodyMutator.EnsureStreamUsageRequested(bodyBytes, endpoint.ApiFamily, operationType, fields.Streaming);

            var outcome = await TryForwardWithRetriesAsync(
                httpContext, endpoint, credential, outboundBody, usageExtractor,
                captureResponseId: operationType == OperationType.ResponsesApi,
                clientRequestedUsage: fields.ClientRequestedUsage,
                streaming: fields.Streaming,
                cancellationToken);

            if (outcome.Succeeded)
            {
                endpointHealthTracker.MarkHealthy(endpoint.Id);
                metrics.RecordRequest(deployment.Name, endpoint.ApiFamily.ToString(), endpoint.BaseUrl, outcome.StatusCode);
                metrics.RecordDuration(stopwatch.Elapsed.TotalMilliseconds, deployment.Name, endpoint.ApiFamily.ToString(), endpoint.BaseUrl, outcome.StatusCode);
                if (outcome.Usage is not null)
                {
                    metrics.RecordTokens(
                        deployment.Name, endpoint.BaseUrl, outcome.Usage.PromptTokens, outcome.Usage.CompletionTokens, outcome.Usage.TotalTokens);
                }

                await PersistResponsesApiAffinityAsync(
                    deployment, endpoint, operationType, fields.StoreFalse, outcome.CapturedResponseId, cancellationToken);

                // ConsumerId is only populated once ingress authentication (Phase 7) resolves a caller; until
                // then there is no valid Consumer to attribute usage to, so capture is skipped rather than
                // violating the UsageRecord.ConsumerId invariant with a placeholder id. Likewise, UsageRecord
                // requires at least one usage signal, so a response the extractor couldn't read any usage from
                // (e.g. no `usage` field at all) is not recorded rather than persisting an empty row.
                if (consumerId is not null && outcome.Usage is not null)
                {
                    EnqueueUsageRecord(
                        deployment, endpoint, operationType, fields, sessionKey, consumerId.Value, consumerName, outcome, stopwatch.ElapsedMilliseconds);
                }

                return null;
            }

            endpointHealthTracker.MarkUnhealthy(endpoint.Id);
            previouslyFailedEndpoint = endpoint;
            firstFailureSnapshot ??= outcome.Failure;

            if (stickyEndpointId is not null)
            {
                // Deliberately not attaching a snapshot here: a stateful session must fail fast with a clear,
                // generic error rather than have a stale/misleading upstream body replayed to the client — see
                // architecture.md §9.
                return new RoutingFailure(
                    RoutingFailureReason.StickyEndpointUnavailable,
                    $"Sticky endpoint '{stickyEndpointId}' failed and failover is disallowed for stateful sessions.");
            }

            excludedEndpointIds.Add(endpoint.Id);
        }
    }

    private IUsageExtractor? TryResolveUsageExtractor(ApiFamily apiFamily, OperationType operationType)
    {
        try
        {
            return usageExtractorResolver.Resolve(apiFamily, operationType);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    private void EnqueueUsageRecord(
        ModelDeployment deployment,
        Endpoint endpoint,
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
            deployment.Name,
            consumerId,
            consumerName ?? "unknown",
            endpoint.Id,
            endpoint.ApiFamily,
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
        EndpointCredential credential,
        byte[] bodyBytes,
        IUsageExtractor? usageExtractor,
        bool captureResponseId,
        bool clientRequestedUsage,
        bool streaming,
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
                using var request = DirectHttpProxy.BuildRequest(httpContext, endpoint.BaseUrl, bodyBytes, credential);
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
        ModelDeployment deployment,
        Endpoint endpoint,
        OperationType operationType,
        bool storeFalse,
        string? capturedResponseId,
        CancellationToken cancellationToken)
    {
        if (operationType != OperationType.ResponsesApi || storeFalse || capturedResponseId is null)
        {
            return;
        }

        var entry = SessionAffinityEntry.Create(capturedResponseId, deployment.Id, endpoint.Id, _sessionAffinityTtl);
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
