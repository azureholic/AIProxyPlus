using AIProxyPlus.Domain.Enums;

namespace AIProxyPlus.Application.Abstractions;

/// <summary>
/// Runtime (in-memory) health tracking for endpoints, driven by passive health checks in <c>ProxyRoutingService</c>
/// (architecture.md §9). A mark made via <see cref="MarkUnhealthy"/> is not permanent — implementations apply a
/// bounded cooldown after which <see cref="GetHealth"/> reports the endpoint as eligible again, so a single past
/// failure can't exclude an endpoint forever.
/// </summary>
public interface IEndpointHealthTracker
{
    EndpointHealthState GetHealth(Guid endpointId, DateTimeOffset? now = null);

    void MarkHealthy(Guid endpointId);

    void MarkUnhealthy(Guid endpointId);
}
