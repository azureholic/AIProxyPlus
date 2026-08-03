using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiProxy.Infrastructure.Repositories;

public sealed class EndpointRepository(AiProxyDbContext dbContext) : IEndpointRepository
{
    public Task<Endpoint?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Endpoints.FirstOrDefaultAsync(endpoint => endpoint.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Endpoint>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Endpoints.OrderBy(endpoint => endpoint.Name).ToListAsync(cancellationToken);

    public Task<bool> ExistsByNameAsync(
        string name,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default) =>
        dbContext.Endpoints.AnyAsync(
            endpoint => endpoint.Name == name && (!excludingId.HasValue || endpoint.Id != excludingId.Value),
            cancellationToken);

    public Task<bool> IsAssignedAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ModelEndpoints.AnyAsync(assignment => assignment.EndpointId == id, cancellationToken);

    public async Task AddAsync(Endpoint endpoint, CancellationToken cancellationToken = default)
    {
        dbContext.Endpoints.Add(endpoint);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Endpoint endpoint, CancellationToken cancellationToken = default)
    {
        if (dbContext.Entry(endpoint).State == EntityState.Detached)
        {
            dbContext.Endpoints.Update(endpoint);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var endpoint = await dbContext.Endpoints.FindAsync([id], cancellationToken);
        if (endpoint is null)
        {
            return;
        }

        dbContext.Endpoints.Remove(endpoint);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}