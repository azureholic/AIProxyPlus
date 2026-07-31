using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiProxy.Infrastructure.Repositories;

public sealed class ConsumerRepository(AiProxyDbContext dbContext) : IConsumerRepository
{
    public Task<Consumer?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.Consumers.FirstOrDefaultAsync(c => c.Name == name, cancellationToken);

    public Task<Consumer?> GetByApiKeyHashAsync(string apiKeyHash, CancellationToken cancellationToken = default) =>
        dbContext.Consumers.FirstOrDefaultAsync(c => c.ApiKeyHash == apiKeyHash, cancellationToken);

    public async Task<IReadOnlyList<Consumer>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Consumers.OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.Consumers.AnyAsync(c => c.Name == name, cancellationToken);

    public async Task AddAsync(Consumer consumer, CancellationToken cancellationToken = default)
    {
        dbContext.Consumers.Add(consumer);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Consumer consumer, CancellationToken cancellationToken = default)
    {
        if (dbContext.Entry(consumer).State == EntityState.Detached)
        {
            dbContext.Consumers.Update(consumer);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
