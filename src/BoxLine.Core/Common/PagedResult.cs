namespace BoxLine.Core.Common;

/// <summary>One page of results together with the total number of matches.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
