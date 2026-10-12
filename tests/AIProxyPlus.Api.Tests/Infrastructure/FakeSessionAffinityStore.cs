using System.Collections.Concurrent;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Entities;

namespace AIProxyPlus.Api.Tests.Infrastructure;

/// <summary>In-memory <see cref="ISessionAffinityStore"/> fake for fast, Docker-free API tests.</summary>
public sealed class FakeSessionAffinityStore : ISessionAffinityStore
{
    private readonly ConcurrentDictionary<string, SessionAffinityEntry> _bySessionId = new();

    public Task<SessionAffinityEntry?> GetAsync(string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_bySessionId.GetValueOrDefault(sessionId));

    public Task SaveAsync(SessionAffinityEntry entry, CancellationToken cancellationToken = default)
    {
        _bySessionId[entry.SessionId] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _bySessionId.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
