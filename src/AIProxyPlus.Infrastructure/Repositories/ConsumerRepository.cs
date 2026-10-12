using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AIProxyPlus.Infrastructure.Repositories;

public sealed class ConsumerRepository(AIProxyPlusDbContext dbContext) : IConsumerRepository
{
    private IQueryable<Consumer> Consumers => dbContext.Consumers.Include("_allowedModels");

    public Task<Consumer?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Consumers.FirstOrDefaultAsync(c => c.Name == name, cancellationToken);

    public Task<Consumer?> GetByApiKeyHashAsync(string apiKeyHash, CancellationToken cancellationToken = default) =>
        Consumers.FirstOrDefaultAsync(c => c.ApiKeyHash == apiKeyHash || c.ApiKeyHash2 == apiKeyHash, cancellationToken);

    public Task<Consumer?> GetByOidcIdentityAsync(
        string oidcIssuer,
        string oidcSubject,
        CancellationToken cancellationToken = default) =>
        Consumers.FirstOrDefaultAsync(
            c => c.OidcIssuer == oidcIssuer && c.OidcSubject == oidcSubject,
            cancellationToken);

    public Task<Consumer?> GetByManagedIdentityAsync(
        Guid tenantId,
        Guid objectId,
        CancellationToken cancellationToken = default) =>
        Consumers.FirstOrDefaultAsync(
            c => c.ManagedIdentityTenantId == tenantId && c.ManagedIdentityObjectId == objectId,
            cancellationToken);

    public async Task<IReadOnlyList<Consumer>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Consumers.OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.Consumers.AnyAsync(c => c.Name == name, cancellationToken);

    public async Task AddAsync(Consumer consumer, CancellationToken cancellationToken = default)
    {
        dbContext.Consumers.Add(consumer);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryAddAsync(Consumer consumer, CancellationToken cancellationToken = default)
    {
        dbContext.Consumers.Add(consumer);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.Entry(consumer).State = EntityState.Detached;
            return false;
        }
    }

    public async Task UpdateAsync(Consumer consumer, CancellationToken cancellationToken = default)
    {
        if (dbContext.Entry(consumer).State == EntityState.Detached)
        {
            dbContext.Consumers.Update(consumer);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Consumer consumer, CancellationToken cancellationToken = default)
    {
        dbContext.Consumers.Remove(consumer);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
