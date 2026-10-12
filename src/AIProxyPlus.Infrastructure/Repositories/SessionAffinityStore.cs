using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIProxyPlus.Infrastructure.Repositories;

public sealed class SessionAffinityStore(AIProxyPlusDbContext dbContext) : ISessionAffinityStore
{
    public Task<SessionAffinityEntry?> GetAsync(string sessionId, CancellationToken cancellationToken = default) =>
        dbContext.SessionAffinityEntries.FirstOrDefaultAsync(e => e.SessionId == sessionId, cancellationToken);

    public async Task SaveAsync(SessionAffinityEntry entry, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.SessionAffinityEntries
            .FirstOrDefaultAsync(e => e.SessionId == entry.SessionId, cancellationToken);

        if (existing is null)
        {
            dbContext.SessionAffinityEntries.Add(entry);
        }
        else if (!ReferenceEquals(existing, entry))
        {
            dbContext.Entry(existing).CurrentValues.SetValues(entry);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.SessionAffinityEntries
            .FirstOrDefaultAsync(e => e.SessionId == sessionId, cancellationToken);

        if (existing is null)
        {
            return;
        }

        dbContext.SessionAffinityEntries.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
