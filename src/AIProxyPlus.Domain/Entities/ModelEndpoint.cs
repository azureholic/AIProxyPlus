using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Domain.Exceptions;

namespace AIProxyPlus.Domain.Entities;

/// <summary>Assigns a reusable endpoint to a model with model-specific routing settings.</summary>
public sealed class ModelEndpoint
{
    public Guid Id { get; private set; }
    public Guid ModelId { get; private set; }
    public Guid EndpointId { get; private set; }
    public Endpoint Endpoint { get; private set; }
    public Guid ApiRouteId { get; private set; }
    public ApiRoute ApiRoute { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; private set; }
    public EndpointHealthState Health { get; private set; }

    public string Name => Endpoint.Name;
    public string BaseUrl => Endpoint.BaseUrl;
    public Provider Provider => ApiRoute.Provider;
    public AuthMode AuthMode => Endpoint.AuthMode;
    public string? SecretRef => Endpoint.SecretRef;
    public string? ManagedIdentityScope => Endpoint.ManagedIdentityScope;
    public string? ManagedIdentityTenantId => Endpoint.ManagedIdentityTenantId;
    public int TimeoutSeconds => Endpoint.TimeoutSeconds;
    public int MaxRetries => Endpoint.MaxRetries;

    private ModelEndpoint()
    {
        Endpoint = null!;
        ApiRoute = null!;
    }

    private ModelEndpoint(Guid id, Guid modelId, Endpoint endpoint, ApiRoute apiRoute, int priority, bool enabled)
    {
        Id = id;
        ModelId = modelId;
        EndpointId = endpoint.Id;
        Endpoint = endpoint;
        ApiRouteId = apiRoute.Id;
        ApiRoute = apiRoute;
        Priority = priority;
        Enabled = enabled;
        Health = EndpointHealthState.Unknown;
    }

    public static ModelEndpoint Create(
        Guid id,
        Guid modelId,
        Endpoint endpoint,
        ApiRoute apiRoute,
        int priority,
        bool enabled = true)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("ModelEndpoint.Id must be valid.");
        }

        if (modelId == Guid.Empty)
        {
            throw new DomainException("ModelEndpoint.ModelId must be valid.");
        }

        if (priority < 1)
        {
            throw new DomainException("ModelEndpoint.Priority must be 1 or greater.");
        }

        return new ModelEndpoint(id, modelId, endpoint, apiRoute, priority, enabled);
    }

    public void UpdatePriority(int priority)
    {
        if (priority < 1)
        {
            throw new DomainException("ModelEndpoint.Priority must be 1 or greater.");
        }

        Priority = priority;
    }

    public void Enable() => Enabled = true;

    public void Disable() => Enabled = false;

    public void MarkHealthy() => Health = EndpointHealthState.Healthy;

    public void MarkUnhealthy() => Health = EndpointHealthState.Unhealthy;
}