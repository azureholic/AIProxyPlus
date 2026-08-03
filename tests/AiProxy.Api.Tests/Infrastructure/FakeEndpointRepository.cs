using System.Collections.Concurrent;
using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;

namespace AiProxy.Api.Tests.Infrastructure;

/// <summary>In-memory <see cref="IEndpointRepository"/> fake for fast, Docker-free API tests.</summary>
public sealed class FakeEndpointRepository(FakeModelRepository models) : IEndpointRepository
{
    private readonly ConcurrentDictionary<Guid, Endpoint> _byId = new();

    public Task<Endpoint?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public Task<IReadOnlyList<Endpoint>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Endpoint>>(_byId.Values.OrderBy(endpoint => endpoint.Name).ToList());

    public Task<bool> ExistsByNameAsync(
        string name,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.Values.Any(endpoint =>
            endpoint.Name == name && (!excludingId.HasValue || endpoint.Id != excludingId.Value)));

    public async Task<bool> IsAssignedAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await models.GetAllAsync(cancellationToken))
            .Any(model => model.Endpoints.Any(assignment => assignment.EndpointId == id));

    public Task AddAsync(Endpoint endpoint, CancellationToken cancellationToken = default)
    {
        _byId[endpoint.Id] = endpoint;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Endpoint endpoint, CancellationToken cancellationToken = default)
    {
        _byId[endpoint.Id] = endpoint;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _byId.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}