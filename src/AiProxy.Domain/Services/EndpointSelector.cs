using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;

namespace AiProxy.Domain.Services;

/// <summary>
/// Pure (no I/O) endpoint-selection logic used for both the initial pick and failover retries
/// (architecture.md §9). Health state and sticky-session data are supplied by the caller.
/// </summary>
public static class EndpointSelector
{
    /// <param name="endpoints">All endpoints configured for the deployment.</param>
    /// <param name="stickyEndpointId">
    /// When set, the call must be pinned to this endpoint (session affinity). If it's missing, disabled, or
    /// unhealthy the selection fails outright — silently rerouting a stateful session is unsafe (§9).
    /// </param>
    /// <param name="excludedEndpointIds">
    /// Endpoints already tried and failed in this request (used for failover retries so the same endpoint
    /// isn't picked twice). Ignored when <paramref name="stickyEndpointId"/> is set.
    /// </param>
    public static EndpointSelectionResult SelectEndpoint(
        IReadOnlyList<Endpoint> endpoints,
        Guid? stickyEndpointId = null,
        IReadOnlySet<Guid>? excludedEndpointIds = null)
    {
        if (stickyEndpointId is { } sticky)
        {
            var stickyEndpoint = endpoints.FirstOrDefault(e => e.Id == sticky && e.Enabled);
            if (stickyEndpoint is null)
            {
                return EndpointSelectionResult.Failure(
                    $"Sticky endpoint '{sticky}' is not a valid, enabled endpoint for this deployment.");
            }

            if (stickyEndpoint.Health == EndpointHealthState.Unhealthy)
            {
                return EndpointSelectionResult.Failure(
                    $"Sticky endpoint '{sticky}' is unhealthy; failing fast per session-affinity policy.");
            }

            return EndpointSelectionResult.Success(stickyEndpoint);
        }

        var candidates = endpoints.Where(e => e.Enabled).ToList();
        if (excludedEndpointIds is { Count: > 0 })
        {
            candidates = candidates.Where(e => !excludedEndpointIds.Contains(e.Id)).ToList();
        }

        if (candidates.Count == 0)
        {
            return EndpointSelectionResult.Failure("No enabled endpoints available for this deployment.");
        }

        var healthy = candidates
            .Where(e => e.Health != EndpointHealthState.Unhealthy)
            .OrderBy(e => e.Priority)
            .ToList();

        return healthy.Count == 0
            ? EndpointSelectionResult.Failure("All endpoints for this deployment are unhealthy.")
            : EndpointSelectionResult.Success(healthy[0]);
    }
}
