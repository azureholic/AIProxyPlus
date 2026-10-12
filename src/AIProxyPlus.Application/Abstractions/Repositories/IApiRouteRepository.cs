using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;

namespace AIProxyPlus.Application.Abstractions;

public interface IApiRouteRepository
{
    Task<ApiRoute?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApiRoute>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, Guid? excludingId = null, CancellationToken cancellationToken = default);
    Task<bool> ExistsByProviderAndPathPrefixAsync(Provider provider, string pathPrefix, Guid? excludingId = null, CancellationToken cancellationToken = default);
    Task<bool> IsAssignedAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ApiRoute apiRoute, CancellationToken cancellationToken = default);
    Task UpdateAsync(ApiRoute apiRoute, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}