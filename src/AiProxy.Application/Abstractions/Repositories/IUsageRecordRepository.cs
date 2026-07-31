using AiProxy.Domain.Entities;

namespace AiProxy.Application.Abstractions;

public interface IUsageRecordRepository
{
    Task AddAsync(UsageRecord usageRecord, CancellationToken cancellationToken = default);

    Task<PagedResult<UsageRecord>> QueryAsync(UsageRecordQuery query, CancellationToken cancellationToken = default);
}
