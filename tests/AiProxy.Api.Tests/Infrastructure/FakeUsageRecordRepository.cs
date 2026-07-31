using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;

namespace AiProxy.Api.Tests.Infrastructure;

/// <summary>In-memory <see cref="IUsageRecordRepository"/> fake so tests can assert on captured usage records.</summary>
public sealed class FakeUsageRecordRepository : IUsageRecordRepository
{
    private readonly List<UsageRecord> _records = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<UsageRecord> Records
    {
        get
        {
            lock (_lock)
            {
                return _records.ToList();
            }
        }
    }

    public Task AddAsync(UsageRecord usageRecord, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _records.Add(usageRecord);
        }

        return Task.CompletedTask;
    }

    public Task<PagedResult<UsageRecord>> QueryAsync(UsageRecordQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<UsageRecord>(Records, Records.Count, 1, Records.Count));
}
