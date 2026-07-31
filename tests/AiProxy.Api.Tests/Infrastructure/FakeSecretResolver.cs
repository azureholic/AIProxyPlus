using System.Collections.Concurrent;
using AiProxy.Application.Abstractions;

namespace AiProxy.Api.Tests.Infrastructure;

/// <summary>In-memory <see cref="ISecretResolver"/> fake for fast, Docker-free API tests.</summary>
public sealed class FakeSecretResolver : ISecretResolver
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    public void Seed(string secretRef, string plaintextValue) => _values[secretRef] = plaintextValue;

    public Task<string?> TryResolveAsync(string secretRef, CancellationToken cancellationToken = default) =>
        Task.FromResult(_values.GetValueOrDefault(secretRef));

    public Task StoreAsync(string secretRef, string plaintextValue, CancellationToken cancellationToken = default)
    {
        _values[secretRef] = plaintextValue;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string secretRef, CancellationToken cancellationToken = default)
    {
        _values.TryRemove(secretRef, out _);
        return Task.CompletedTask;
    }
}
