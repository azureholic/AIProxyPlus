using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiProxy.Infrastructure.Repositories;

public sealed class UsageRecordRepository(AiProxyDbContext dbContext) : IUsageRecordRepository
{
    public async Task AddAsync(UsageRecord usageRecord, CancellationToken cancellationToken = default)
    {
        dbContext.UsageRecords.Add(usageRecord);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<UsageRecord>> QueryAsync(UsageRecordQuery query, CancellationToken cancellationToken = default)
    {
        var filtered = dbContext.UsageRecords.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.ModelName))
        {
            filtered = filtered.Where(u => u.ModelName == query.ModelName);
        }

        if (!string.IsNullOrWhiteSpace(query.ConsumerName))
        {
            filtered = filtered.Where(u => u.ConsumerName == query.ConsumerName);
        }

        if (query.OperationType.HasValue)
        {
            filtered = filtered.Where(u => u.OperationType == query.OperationType.Value);
        }

        if (query.From.HasValue)
        {
            filtered = filtered.Where(u => u.Timestamp >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            filtered = filtered.Where(u => u.Timestamp <= query.To.Value);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .OrderByDescending(u => u.Timestamp)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<UsageRecord>(items, totalCount, query.Page, query.PageSize);
    }
}
