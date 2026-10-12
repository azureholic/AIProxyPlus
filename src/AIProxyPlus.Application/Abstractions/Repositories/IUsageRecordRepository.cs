using AIProxyPlus.Domain.Entities;

namespace AIProxyPlus.Application.Abstractions;

public interface IUsageRecordRepository
{
    Task AddAsync(UsageRecord usageRecord, CancellationToken cancellationToken = default);

    Task<PagedResult<UsageRecord>> QueryAsync(UsageRecordQuery query, CancellationToken cancellationToken = default);
}
