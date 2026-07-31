using AiProxy.Domain.Entities;

namespace AiProxy.Application.Abstractions;

public interface IModelDeploymentRepository
{
    Task<ModelDeployment?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelDeployment>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);

    Task AddAsync(ModelDeployment modelDeployment, CancellationToken cancellationToken = default);

    Task UpdateAsync(ModelDeployment modelDeployment, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid modelDeploymentId, CancellationToken cancellationToken = default);
}
