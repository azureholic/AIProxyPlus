using AIProxyPlus.Domain.Entities;

namespace AIProxyPlus.Application.Abstractions;

public interface IEndpointRepository
{
    Task<Endpoint?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Endpoint>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameAsync(string name, Guid? excludingId = null, CancellationToken cancellationToken = default);

    Task<bool> IsAssignedAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> IsSecretReferenceInUseAsync(string secretRef, CancellationToken cancellationToken = default);

    Task AddAsync(Endpoint endpoint, CancellationToken cancellationToken = default);

    Task UpdateAsync(Endpoint endpoint, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}