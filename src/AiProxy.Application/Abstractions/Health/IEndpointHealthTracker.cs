using AiProxy.Domain.Enums;

namespace AiProxy.Application.Abstractions;

/// <summary>Runtime (in-memory) health tracking for endpoints, driven by passive health checks in <c>ProxyRoutingService</c> (§9).</summary>
public interface IEndpointHealthTracker
{
    EndpointHealthState GetHealth(Guid endpointId);

    void MarkHealthy(Guid endpointId);

    void MarkUnhealthy(Guid endpointId);
}
