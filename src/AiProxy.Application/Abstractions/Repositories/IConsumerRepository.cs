using AiProxy.Domain.Entities;

namespace AiProxy.Application.Abstractions;

public interface IConsumerRepository
{
    Task<Consumer?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<Consumer?> GetByApiKeyHashAsync(string apiKeyHash, CancellationToken cancellationToken = default);

    Task<Consumer?> GetByOidcIdentityAsync(
        string oidcIssuer,
        string oidcSubject,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Consumer>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);

    Task AddAsync(Consumer consumer, CancellationToken cancellationToken = default);

    Task<bool> TryAddAsync(Consumer consumer, CancellationToken cancellationToken = default);

    Task UpdateAsync(Consumer consumer, CancellationToken cancellationToken = default);

    Task DeleteAsync(Consumer consumer, CancellationToken cancellationToken = default);
}
