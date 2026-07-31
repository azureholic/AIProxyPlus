using System.Collections.Concurrent;
using AiProxy.Application.Abstractions;

namespace AiProxy.Infrastructure.Routing;

/// <summary>In-memory, per-deployment round-robin counter (§9) — process-local, no cross-instance fairness guarantee.</summary>
public sealed class EndpointRoundRobinCounter : IEndpointRoundRobinCounter
{
    private readonly ConcurrentDictionary<Guid, int> _counters = new();

    public int Next(Guid modelDeploymentId) =>
        _counters.AddOrUpdate(modelDeploymentId, 0, (_, current) => unchecked(current + 1));
}
