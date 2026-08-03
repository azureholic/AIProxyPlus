using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiProxy.Infrastructure.Repositories;

public sealed class ApiRouteRepository(AiProxyDbContext dbContext) : IApiRouteRepository
{
    public Task<ApiRoute?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ApiRoutes.FirstOrDefaultAsync(route => route.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ApiRoute>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.ApiRoutes.OrderBy(route => route.Name).ToListAsync(cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, Guid? excludingId = null, CancellationToken cancellationToken = default) =>
        dbContext.ApiRoutes.AnyAsync(
            route => EF.Functions.ILike(route.Name, name) && (!excludingId.HasValue || route.Id != excludingId.Value),
            cancellationToken);

    public Task<bool> ExistsByProviderAndPathPrefixAsync(
        Provider provider,
        string pathPrefix,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default) =>
        dbContext.ApiRoutes.AnyAsync(
            route => route.Provider == provider && route.PathPrefix == pathPrefix &&
                (!excludingId.HasValue || route.Id != excludingId.Value),
            cancellationToken);

    public Task<bool> IsAssignedAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ModelEndpoints.AnyAsync(assignment => assignment.ApiRouteId == id, cancellationToken);

    public async Task AddAsync(ApiRoute apiRoute, CancellationToken cancellationToken = default)
    {
        dbContext.ApiRoutes.Add(apiRoute);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ApiRoute apiRoute, CancellationToken cancellationToken = default)
    {
        if (dbContext.Entry(apiRoute).State == EntityState.Detached)
        {
            dbContext.ApiRoutes.Update(apiRoute);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var route = await dbContext.ApiRoutes.FindAsync([id], cancellationToken);
        if (route is null)
        {
            return;
        }

        dbContext.ApiRoutes.Remove(route);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}