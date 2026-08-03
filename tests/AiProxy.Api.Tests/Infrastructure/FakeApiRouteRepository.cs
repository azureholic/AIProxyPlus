using System.Collections.Concurrent;
using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;

namespace AiProxy.Api.Tests.Infrastructure;

public sealed class FakeApiRouteRepository(FakeModelRepository models) : IApiRouteRepository
{
    private readonly ConcurrentDictionary<Guid, ApiRoute> _byId = new();

    public Task<ApiRoute?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public Task<IReadOnlyList<ApiRoute>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ApiRoute>>(_byId.Values.OrderBy(route => route.Name).ToList());

    public Task<bool> ExistsByNameAsync(
        string name,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.Values.Any(route =>
            route.Name == name && (!excludingId.HasValue || route.Id != excludingId.Value)));

    public Task<bool> ExistsByProviderAndPathPrefixAsync(
        Provider provider,
        string pathPrefix,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.Values.Any(route =>
            route.Provider == provider
            && route.PathPrefix == pathPrefix
            && (!excludingId.HasValue || route.Id != excludingId.Value)));

    public async Task<bool> IsAssignedAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await models.GetAllAsync(cancellationToken))
            .Any(model => model.Endpoints.Any(assignment => assignment.ApiRouteId == id));

    public Task AddAsync(ApiRoute apiRoute, CancellationToken cancellationToken = default)
    {
        _byId[apiRoute.Id] = apiRoute;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ApiRoute apiRoute, CancellationToken cancellationToken = default)
    {
        _byId[apiRoute.Id] = apiRoute;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _byId.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}