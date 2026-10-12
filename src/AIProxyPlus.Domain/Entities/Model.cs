using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Domain.Exceptions;

namespace AIProxyPlus.Domain.Entities;

/// <summary>A named routing target with reusable endpoint assignments.</summary>
public sealed class Model
{
    private readonly List<ModelEndpoint> _endpoints = [];
    private readonly List<ModelCapabilityAssignment> _capabilities = [];

    private static readonly ModelCapability[] DefaultCapabilities =
    [
        ModelCapability.Chat,
        ModelCapability.ImageGeneration,
        ModelCapability.Realtime
    ];

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public RoutingStrategy Strategy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<ModelEndpoint> Endpoints => _endpoints;
    public IReadOnlyList<ModelCapability> Capabilities => _capabilities.Select(item => item.Capability).ToArray();

    /// <summary>For EF Core materialization only — domain code must use <see cref="Create"/>.</summary>
    private Model()
    {
        Name = null!;
    }

    private Model(
        Guid id,
        string name,
        RoutingStrategy strategy,
        DateTimeOffset createdAt,
        IEnumerable<ModelCapability> capabilities)
    {
        Id = id;
        Name = name;
        Strategy = strategy;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ReplaceCapabilitiesCore(capabilities);
    }

    public static Model Create(
        Guid id,
        string name,
        RoutingStrategy strategy = RoutingStrategy.Priority,
        DateTimeOffset? createdAt = null,
        IEnumerable<ModelCapability>? capabilities = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Model.Name must not be empty.");
        }

        return new Model(id, name, strategy, createdAt ?? DateTimeOffset.UtcNow, capabilities ?? DefaultCapabilities);
    }

    public void ReplaceCapabilities(IEnumerable<ModelCapability> capabilities)
    {
        ReplaceCapabilitiesCore(capabilities);
        Touch();
    }

    /// <summary>True once at least one enabled endpoint is registered; false models can't be routed to.</summary>
    public bool HasUsableEndpoint => _endpoints.Any(e => e.Enabled);

    /// <summary>Adds a candidate endpoint. Multiple endpoints may share the same <see cref="Endpoint.Priority"/> —
    /// <see cref="Services.EndpointSelector"/> round-robins across endpoints tied for the lowest priority (§9).</summary>
    public void AddEndpoint(ModelEndpoint endpoint)
    {
        if (endpoint.ModelId != Id)
        {
            throw new DomainException("ModelEndpoint.ModelId does not match this Model.");
        }

        if (_endpoints.Any(existing =>
                existing.EndpointId == endpoint.EndpointId && existing.ApiRouteId == endpoint.ApiRouteId))
        {
            throw new DomainException(
                $"Endpoint '{endpoint.EndpointId}' and API route '{endpoint.ApiRouteId}' are already assigned to this Model.");
        }

        _endpoints.Add(endpoint);
        Touch();
    }

    public void RemoveEndpoint(Guid assignmentId)
    {
        _endpoints.RemoveAll(e => e.Id == assignmentId);
        Touch();
    }

    /// <summary>Changes an endpoint's priority. Ties with other endpoints are allowed (they round-robin together, §9).</summary>
    public void ChangeEndpointPriority(Guid assignmentId, int newPriority)
    {
        var endpoint = _endpoints.FirstOrDefault(e => e.Id == assignmentId)
            ?? throw new DomainException($"Assignment '{assignmentId}' is not assigned to this Model.");

        endpoint.UpdatePriority(newPriority);
        Touch();
    }

    public void SetStrategy(RoutingStrategy strategy)
    {
        Strategy = strategy;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private void ReplaceCapabilitiesCore(IEnumerable<ModelCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var values = capabilities.ToArray();
        if (values.Length == 0)
        {
            throw new DomainException("Model must have at least one capability.");
        }

        if (values.Distinct().Count() != values.Length)
        {
            throw new DomainException("Model capabilities must not contain duplicates.");
        }

        _capabilities.Clear();
        _capabilities.AddRange(values.Select(capability => new ModelCapabilityAssignment(Id, capability)));
    }
}
