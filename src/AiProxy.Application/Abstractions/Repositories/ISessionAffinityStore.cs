using AiProxy.Domain.Entities;

namespace AiProxy.Application.Abstractions;

public interface ISessionAffinityStore
{
    Task<SessionAffinityEntry?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    Task SaveAsync(SessionAffinityEntry entry, CancellationToken cancellationToken = default);

    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}
