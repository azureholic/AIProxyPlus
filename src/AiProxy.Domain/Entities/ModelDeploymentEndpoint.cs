using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;

namespace AiProxy.Domain.Entities;

/// <summary>Assigns a reusable endpoint to a model deployment with deployment-specific routing settings.</summary>
public sealed class ModelDeploymentEndpoint
{
    public Guid ModelDeploymentId { get; private set; }
    public Guid EndpointId { get; private set; }
    public Endpoint Endpoint { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; private set; }
    public EndpointHealthState Health { get; private set; }

    public Guid Id => EndpointId;
    public string Name => Endpoint.Name;
    public string BaseUrl => Endpoint.BaseUrl;
    public ApiFamily ApiFamily => Endpoint.ApiFamily;
    public AuthMode AuthMode => Endpoint.AuthMode;
    public string? SecretRef => Endpoint.SecretRef;
    public string? ManagedIdentityScope => Endpoint.ManagedIdentityScope;
    public string? ManagedIdentityTenantId => Endpoint.ManagedIdentityTenantId;
    public int TimeoutSeconds => Endpoint.TimeoutSeconds;
    public int MaxRetries => Endpoint.MaxRetries;

    private ModelDeploymentEndpoint()
    {
        Endpoint = null!;
    }

    private ModelDeploymentEndpoint(Guid modelDeploymentId, Endpoint endpoint, int priority, bool enabled)
    {
        ModelDeploymentId = modelDeploymentId;
        EndpointId = endpoint.Id;
        Endpoint = endpoint;
        Priority = priority;
        Enabled = enabled;
        Health = EndpointHealthState.Unknown;
    }

    public static ModelDeploymentEndpoint Create(
        Guid modelDeploymentId,
        Endpoint endpoint,
        int priority,
        bool enabled = true)
    {
        if (modelDeploymentId == Guid.Empty)
        {
            throw new DomainException("ModelDeploymentEndpoint.ModelDeploymentId must be valid.");
        }

        if (priority < 1)
        {
            throw new DomainException("ModelDeploymentEndpoint.Priority must be 1 or greater.");
        }

        return new ModelDeploymentEndpoint(modelDeploymentId, endpoint, priority, enabled);
    }

    public void UpdatePriority(int priority)
    {
        if (priority < 1)
        {
            throw new DomainException("ModelDeploymentEndpoint.Priority must be 1 or greater.");
        }

        Priority = priority;
    }

    public void Enable() => Enabled = true;

    public void Disable() => Enabled = false;

    public void MarkHealthy() => Health = EndpointHealthState.Healthy;

    public void MarkUnhealthy() => Health = EndpointHealthState.Unhealthy;
}