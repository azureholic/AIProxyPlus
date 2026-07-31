using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;

namespace AiProxy.Domain.Entities;

/// <summary>A named routing target (§6.1 "model-deployment name") with one or more candidate <see cref="Endpoint"/>s.</summary>
public sealed class ModelDeployment
{
    private readonly List<Endpoint> _endpoints = [];

    public Guid Id { get; }
    public string Name { get; }
    public bool StickySessionEnabled { get; private set; }
    public RoutingStrategy Strategy { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<Endpoint> Endpoints => _endpoints;

    private ModelDeployment(
        Guid id,
        string name,
        bool stickySessionEnabled,
        RoutingStrategy strategy,
        DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        StickySessionEnabled = stickySessionEnabled;
        Strategy = strategy;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static ModelDeployment Create(
        Guid id,
        string name,
        bool stickySessionEnabled = false,
        RoutingStrategy strategy = RoutingStrategy.Priority,
        DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("ModelDeployment.Name must not be empty.");
        }

        return new ModelDeployment(id, name, stickySessionEnabled, strategy, createdAt ?? DateTimeOffset.UtcNow);
    }

    /// <summary>True once at least one enabled endpoint is registered; false deployments can't be routed to.</summary>
    public bool HasUsableEndpoint => _endpoints.Any(e => e.Enabled);

    public void AddEndpoint(Endpoint endpoint)
    {
        if (endpoint.ModelDeploymentId != Id)
        {
            throw new DomainException("Endpoint.ModelDeploymentId does not match this ModelDeployment.");
        }

        if (_endpoints.Any(e => e.Priority == endpoint.Priority))
        {
            throw new DomainException($"Priority {endpoint.Priority} is already used by another endpoint on this deployment.");
        }

        _endpoints.Add(endpoint);
        Touch();
    }

    public void RemoveEndpoint(Guid endpointId)
    {
        _endpoints.RemoveAll(e => e.Id == endpointId);
        Touch();
    }

    public void SetStickySessionEnabled(bool enabled)
    {
        StickySessionEnabled = enabled;
        Touch();
    }

    public void SetStrategy(RoutingStrategy strategy)
    {
        Strategy = strategy;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
