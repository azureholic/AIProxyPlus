namespace AIProxyPlus.Application.Abstractions;

/// <summary>
/// Per-model round-robin counter used to rotate endpoint selection across endpoints sharing the lowest
/// priority (architecture.md §9). One value per incoming request, not per retry attempt — see
/// <see cref="AIProxyPlus.Domain.Services.EndpointSelector"/> for how the snapshot is consumed.
/// </summary>
public interface IEndpointRoundRobinCounter
{
    int Next(Guid modelId);
}
