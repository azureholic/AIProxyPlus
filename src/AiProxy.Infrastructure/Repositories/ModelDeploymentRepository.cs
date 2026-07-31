using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiProxy.Infrastructure.Repositories;

public sealed class ModelDeploymentRepository(AiProxyDbContext dbContext) : IModelDeploymentRepository
{
    public Task<ModelDeployment?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.ModelDeployments
            .Include(d => d.Endpoints)
            .FirstOrDefaultAsync(d => d.Name == name, cancellationToken);

    public async Task<IReadOnlyList<ModelDeployment>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.ModelDeployments
            .Include(d => d.Endpoints)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.ModelDeployments.AnyAsync(d => d.Name == name, cancellationToken);

    public async Task AddAsync(ModelDeployment modelDeployment, CancellationToken cancellationToken = default)
    {
        dbContext.ModelDeployments.Add(modelDeployment);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ModelDeployment modelDeployment, CancellationToken cancellationToken = default)
    {
        if (dbContext.Entry(modelDeployment).State == EntityState.Detached)
        {
            dbContext.ModelDeployments.Update(modelDeployment);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid modelDeploymentId, CancellationToken cancellationToken = default)
    {
        var deployment = await dbContext.ModelDeployments.FindAsync([modelDeploymentId], cancellationToken);
        if (deployment is null)
        {
            return;
        }

        dbContext.ModelDeployments.Remove(deployment);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
