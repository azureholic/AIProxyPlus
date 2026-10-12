using AIProxyPlus.Domain.Enums;

namespace AIProxyPlus.Application.Abstractions;

/// <summary>Filter/paging criteria for querying usage records (architecture.md §7.2 usage query endpoint).</summary>
public sealed record UsageRecordQuery(
    string? ModelName = null,
    string? ConsumerName = null,
    OperationType? OperationType = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50);
