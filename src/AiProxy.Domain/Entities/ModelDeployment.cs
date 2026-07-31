using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;

namespace AiProxy.Domain.Entities;

/// <summary>A named routing target (§6.1 "model-deployment name") with one or more candidate <see cref="Endpoint"/>s.</summary>
public sealed class ModelDeployment
{
    private readonly List<Endpoint> _endpoints = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public RoutingStrategy Strategy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<Endpoint> Endpoints => _endpoints;

    /// <summary>For EF Core materialization only — domain code must use <see cref="Create"/>.</summary>
    private ModelDeployment()
    {
        Name = null!;
    }

    private ModelDeployment(
        Guid id,
        string name,
        RoutingStrategy strategy,
        DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Strategy = strategy;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static ModelDeployment Create(
        Guid id,
        string name,
        RoutingStrategy strategy = RoutingStrategy.Priority,
        DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("ModelDeployment.Name must not be empty.");
        }

        return new ModelDeployment(id, name, strategy, createdAt ?? DateTimeOffset.UtcNow);
    }

    /// <summary>True once at least one enabled endpoint is registered; false deployments can't be routed to.</summary>
    public bool HasUsableEndpoint => _endpoints.Any(e => e.Enabled);

    /// <summary>Adds a candidate endpoint. Multiple endpoints may share the same <see cref="Endpoint.Priority"/> —
    /// <see cref="Services.EndpointSelector"/> round-robins across endpoints tied for the lowest priority (§9).</summary>
    public void AddEndpoint(Endpoint endpoint)
    {
        if (endpoint.ModelDeploymentId != Id)
        {
            throw new DomainException("Endpoint.ModelDeploymentId does not match this ModelDeployment.");
        }

        _endpoints.Add(endpoint);
        Touch();
    }

    public void RemoveEndpoint(Guid endpointId)
    {
        _endpoints.RemoveAll(e => e.Id == endpointId);
        Touch();
    }

    /// <summary>Changes an endpoint's priority. Ties with other endpoints are allowed (they round-robin together, §9).</summary>
    public void ChangeEndpointPriority(Guid endpointId, int newPriority)
    {
        var endpoint = _endpoints.FirstOrDefault(e => e.Id == endpointId)
            ?? throw new DomainException($"Endpoint '{endpointId}' does not belong to this ModelDeployment.");

        endpoint.UpdatePriority(newPriority);
        Touch();
    }

    public void SetStrategy(RoutingStrategy strategy)
    {
        Strategy = strategy;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
