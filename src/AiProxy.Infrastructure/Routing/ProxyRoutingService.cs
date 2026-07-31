using System.Net.Http;
using System.Text;
using System.Text.Json;
using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Domain.Services;
using Microsoft.AspNetCore.Http;
using Endpoint = AiProxy.Domain.Entities.Endpoint;

namespace AiProxy.Infrastructure.Routing;

/// <summary>
/// Orchestrates deployment resolution, priority/failover selection, sticky sessions, and forwarding for a
/// single ingress request (architecture.md §9). Forwarding itself goes through <see cref="DirectHttpProxy"/>
/// rather than YARP's <c>IHttpForwarder</c> — see that type's remarks for why: retries require inspecting an
/// upstream response before committing anything to the client, which <c>IHttpForwarder</c> does not allow.
/// </summary>
public sealed class ProxyRoutingService(
    IModelDeploymentRepository modelDeploymentRepository,
    ISessionAffinityStore sessionAffinityStore,
    IEndpointHealthTracker endpointHealthTracker,
    ICredentialProvider credentialProvider,
    HttpMessageInvoker httpClient,
    TimeSpan? sessionAffinityTtl = null)
{
    private readonly TimeSpan _sessionAffinityTtl = sessionAffinityTtl ?? TimeSpan.FromMinutes(30);

    public async Task<RoutingFailure?> RouteAsync(HttpContext httpContext, OperationType operationType, CancellationToken cancellationToken)
    {
        var body = await RequestBodyPeek.BufferBodyAsync(httpContext.Request, cancellationToken);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var fields = RequestBodyPeek.ExtractFields(body);

        if (string.IsNullOrWhiteSpace(fields.Model))
        {
            return new RoutingFailure(RoutingFailureReason.DeploymentNotFound, "Request body must include a 'model' field.");
        }

        var deployment = await modelDeploymentRepository.GetByNameAsync(fields.Model, cancellationToken);
        if (deployment is null)
        {
            return new RoutingFailure(RoutingFailureReason.DeploymentNotFound, $"Model deployment '{fields.Model}' was not found.");
        }

        var sessionKey = ResolveSessionKey(httpContext, fields, operationType);
        SessionAffinityEntry? affinityEntry = null;
        Guid? stickyEndpointId = null;

        if (deployment.StickySessionEnabled && sessionKey is not null)
        {
            affinityEntry = await sessionAffinityStore.GetAsync(sessionKey, cancellationToken);
            if (affinityEntry is not null && !affinityEntry.IsExpired())
            {
                stickyEndpointId = affinityEntry.EndpointId;
            }
        }

        var excludedEndpointIds = new HashSet<Guid>();
        while (true)
        {
            var selection = EndpointSelector.SelectEndpoint(deployment.Endpoints, stickyEndpointId, excludedEndpointIds);
            if (!selection.IsSuccess)
            {
                var reason = stickyEndpointId is not null
                    ? RoutingFailureReason.StickyEndpointUnavailable
                    : RoutingFailureReason.NoHealthyEndpoint;
                return new RoutingFailure(reason, selection.FailureReason!);
            }

            var endpoint = selection.SelectedEndpoint!;
            var credential = await credentialProvider.GetCredentialAsync(endpoint, cancellationToken);

            string? capturedResponseId = null;
            Func<string, Task>? onResponseBodyPeek = operationType == OperationType.ResponsesApi && !fields.Streaming
                ? body2 =>
                {
                    capturedResponseId = TryExtractResponseId(body2);
                    return Task.CompletedTask;
                }
                : null;

            var succeeded = await TryForwardWithRetriesAsync(httpContext, endpoint, credential, bodyBytes, onResponseBodyPeek, cancellationToken);
            if (succeeded)
            {
                endpointHealthTracker.MarkHealthy(endpoint.Id);
                await PersistAffinityOnSuccessAsync(
                    deployment, endpoint, operationType, fields, sessionKey, affinityEntry, capturedResponseId, cancellationToken);
                return null;
            }

            endpointHealthTracker.MarkUnhealthy(endpoint.Id);

            if (stickyEndpointId is not null)
            {
                return new RoutingFailure(
                    RoutingFailureReason.StickyEndpointUnavailable,
                    $"Sticky endpoint '{stickyEndpointId}' failed and failover is disallowed for stateful sessions.");
            }

            excludedEndpointIds.Add(endpoint.Id);
        }
    }

    private async Task<bool> TryForwardWithRetriesAsync(
        HttpContext httpContext,
        Endpoint endpoint,
        EndpointCredential credential,
        byte[] bodyBytes,
        Func<string, Task>? onResponseBodyPeek,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= endpoint.MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(JitteredBackoff(attempt), cancellationToken);
            }

            HttpResponseMessage? response;
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
                if ((int)response.StatusCode >= 500)
                {
                    continue;
                }

                if (onResponseBodyPeek is not null)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    await onResponseBodyPeek(responseBody);
                    await DirectHttpProxy.CopyBufferedResponseAsync(response, responseBody, httpContext, cancellationToken);
                }
                else
                {
                    await DirectHttpProxy.CopyResponseAsync(response, httpContext, cancellationToken);
                }

                return true;
            }
        }

        return false;
    }

    private async Task PersistAffinityOnSuccessAsync(
        ModelDeployment deployment,
        Endpoint endpoint,
        OperationType operationType,
        RequestBodyFields fields,
        string? sessionKey,
        SessionAffinityEntry? existingEntry,
        string? capturedResponseId,
        CancellationToken cancellationToken)
    {
        if (fields.StoreFalse)
        {
            return;
        }

        if (operationType == OperationType.ResponsesApi)
        {
            if (capturedResponseId is not null)
            {
                var entry = SessionAffinityEntry.Create(capturedResponseId, deployment.Id, endpoint.Id, _sessionAffinityTtl);
                await sessionAffinityStore.SaveAsync(entry, cancellationToken);
            }

            return;
        }

        if (!deployment.StickySessionEnabled || sessionKey is null)
        {
            return;
        }

        if (existingEntry is not null)
        {
            existingEntry.Touch(_sessionAffinityTtl);
            await sessionAffinityStore.SaveAsync(existingEntry, cancellationToken);
        }
        else
        {
            var entry = SessionAffinityEntry.Create(sessionKey, deployment.Id, endpoint.Id, _sessionAffinityTtl);
            await sessionAffinityStore.SaveAsync(entry, cancellationToken);
        }
    }

    private static string? ResolveSessionKey(HttpContext httpContext, RequestBodyFields fields, OperationType operationType)
    {
        if (operationType == OperationType.ResponsesApi)
        {
            return fields.PreviousResponseId;
        }

        if (!string.IsNullOrWhiteSpace(fields.SessionId))
        {
            return fields.SessionId;
        }

        return httpContext.Request.Headers.TryGetValue("X-Session-Id", out var headerValue) && headerValue.Count > 0
            ? headerValue.ToString()
            : null;
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

    private static TimeSpan JitteredBackoff(int attempt)
    {
        var baseDelayMs = Math.Min(100 * Math.Pow(2, attempt - 1), 2000);
        var jitterMs = Random.Shared.Next(0, 50);
        return TimeSpan.FromMilliseconds(baseDelayMs + jitterMs);
    }
}
