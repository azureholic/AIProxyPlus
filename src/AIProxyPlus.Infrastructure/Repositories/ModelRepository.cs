using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIProxyPlus.Infrastructure.Repositories;

public sealed class ModelRepository(AIProxyPlusDbContext dbContext) : IModelRepository
{
    public Task<Model?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.Models
            .Include("_capabilities")
            .Include(d => d.Endpoints)
            .ThenInclude(e => e.Endpoint)
            .Include(d => d.Endpoints)
            .ThenInclude(e => e.ApiRoute)
            .FirstOrDefaultAsync(d => d.Name == name, cancellationToken);

    public async Task<IReadOnlyList<Model>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Models
            .Include("_capabilities")
            .Include(d => d.Endpoints)
            .ThenInclude(e => e.Endpoint)
            .Include(d => d.Endpoints)
            .ThenInclude(e => e.ApiRoute)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.Models.AnyAsync(d => d.Name == name, cancellationToken);

    public async Task AddAsync(Model model, CancellationToken cancellationToken = default)
    {
        dbContext.Models.Add(model);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Model model, CancellationToken cancellationToken = default)
    {
        if (dbContext.Entry(model).State == EntityState.Detached)
        {
            dbContext.Models.Update(model);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid modelId, CancellationToken cancellationToken = default)
    {
        var model = await dbContext.Models.FindAsync([modelId], cancellationToken);
        if (model is null)
        {
            return;
        }

        dbContext.Models.Remove(model);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
