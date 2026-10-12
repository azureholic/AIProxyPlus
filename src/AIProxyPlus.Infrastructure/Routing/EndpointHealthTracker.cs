using System.Collections.Concurrent;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Enums;

namespace AIProxyPlus.Infrastructure.Routing;

/// <summary>
/// In-memory endpoint health tracking, updated by passive health checks in <see cref="ProxyRoutingService"/>/
/// <see cref="RealtimeProxyService"/> (architecture.md §9). An unhealthy mark expires after
/// <paramref name="unhealthyCooldown"/> so a transient failure doesn't permanently exclude an endpoint — nothing
/// currently calls <see cref="MarkHealthy"/> from a background process, so without this cooldown an endpoint
/// marked unhealthy once would never become eligible again.
/// </summary>
public sealed class EndpointHealthTracker(TimeSpan? unhealthyCooldown = null) : IEndpointHealthTracker
{
    private readonly TimeSpan _unhealthyCooldown = unhealthyCooldown ?? TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<Guid, (EndpointHealthState State, DateTimeOffset MarkedAt)> _health = new();

    public EndpointHealthState GetHealth(Guid endpointId, DateTimeOffset? now = null)
    {
        if (!_health.TryGetValue(endpointId, out var entry))
        {
            return EndpointHealthState.Unknown;
        }

        if (entry.State == EndpointHealthState.Unhealthy && (now ?? DateTimeOffset.UtcNow) - entry.MarkedAt >= _unhealthyCooldown)
        {
            return EndpointHealthState.Unknown;
        }

        return entry.State;
    }

    public void MarkHealthy(Guid endpointId) => _health[endpointId] = (EndpointHealthState.Healthy, DateTimeOffset.UtcNow);

    public void MarkUnhealthy(Guid endpointId) => _health[endpointId] = (EndpointHealthState.Unhealthy, DateTimeOffset.UtcNow);
}
