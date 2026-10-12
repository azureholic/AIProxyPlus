using AIProxyPlus.Domain.Entities;

namespace AIProxyPlus.Application.Abstractions;

public interface IModelRepository
{
    Task<Model?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Model>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);

    Task AddAsync(Model model, CancellationToken cancellationToken = default);

    Task UpdateAsync(Model model, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid modelId, CancellationToken cancellationToken = default);
}
