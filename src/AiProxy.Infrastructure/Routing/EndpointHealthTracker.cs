using System.Collections.Concurrent;
using AiProxy.Application.Abstractions;
using AiProxy.Domain.Enums;

namespace AiProxy.Infrastructure.Routing;

/// <summary>In-memory endpoint health tracking, updated by passive health checks in <see cref="ProxyRoutingService"/>.</summary>
public sealed class EndpointHealthTracker : IEndpointHealthTracker
{
    private readonly ConcurrentDictionary<Guid, EndpointHealthState> _health = new();

    public EndpointHealthState GetHealth(Guid endpointId) =>
        _health.TryGetValue(endpointId, out var health) ? health : EndpointHealthState.Unknown;

    public void MarkHealthy(Guid endpointId) => _health[endpointId] = EndpointHealthState.Healthy;

    public void MarkUnhealthy(Guid endpointId) => _health[endpointId] = EndpointHealthState.Unhealthy;
}
