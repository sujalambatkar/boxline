using BoxLine.Core.Entities;

namespace BoxLine.Core.Containers;

public enum ContainerSortField
{
    Number,
    Type,
    Ownership,
    Status,
}

/// <summary>Server-side search, filter, sort and paging options for the container list.</summary>
public sealed record ContainerQuery(
    string? Search = null,
    int? ContainerTypeId = null,
    ContainerStatus? Status = null,
    ContainerSortField SortBy = ContainerSortField.Number,
    bool Descending = false,
    int Page = 1,
    int PageSize = 20);
