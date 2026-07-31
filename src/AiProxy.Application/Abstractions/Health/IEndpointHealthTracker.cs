using AiProxy.Domain.Enums;

namespace AiProxy.Application.Abstractions;

/// <summary>Runtime (in-memory) health tracking for endpoints, driven by YARP active/passive health checks (§9).</summary>
public interface IEndpointHealthTracker
{
    EndpointHealthState GetHealth(Guid endpointId);

    void MarkHealthy(Guid endpointId);

    void MarkUnhealthy(Guid endpointId);
}
