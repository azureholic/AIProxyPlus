using System.Collections.Concurrent;
using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;

namespace AiProxy.Api.Tests.Infrastructure;

/// <summary>In-memory <see cref="IConsumerRepository"/> fake for fast, Docker-free API tests.</summary>
public sealed class FakeConsumerRepository : IConsumerRepository
{
    private readonly ConcurrentDictionary<string, Consumer> _byName = new();

    public void Seed(Consumer consumer) => _byName[consumer.Name] = consumer;

    public Task<Consumer?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byName.GetValueOrDefault(name));

    public Task<Consumer?> GetByApiKeyHashAsync(string apiKeyHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byName.Values.FirstOrDefault(c => c.ApiKeyHash == apiKeyHash || c.ApiKeyHash2 == apiKeyHash));

    public Task<Consumer?> GetByOidcIdentityAsync(
        string oidcIssuer,
        string oidcSubject,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_byName.Values.FirstOrDefault(
            c => c.OidcIssuer == oidcIssuer && c.OidcSubject == oidcSubject));

    public Task<IReadOnlyList<Consumer>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Consumer>>(_byName.Values.ToList());

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byName.ContainsKey(name));

    public Task AddAsync(Consumer consumer, CancellationToken cancellationToken = default)
    {
        _byName[consumer.Name] = consumer;
        return Task.CompletedTask;
    }

    public Task<bool> TryAddAsync(Consumer consumer, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byName.TryAdd(consumer.Name, consumer));

    public Task UpdateAsync(Consumer consumer, CancellationToken cancellationToken = default)
    {
        _byName[consumer.Name] = consumer;
        return Task.CompletedTask;
    }
}
