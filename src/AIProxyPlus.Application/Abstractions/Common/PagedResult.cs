namespace AIProxyPlus.Application.Abstractions;

/// <summary>A page of results plus enough metadata to compute total pages client-side.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
