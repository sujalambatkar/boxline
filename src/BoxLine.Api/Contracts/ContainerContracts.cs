using System.ComponentModel.DataAnnotations;
using BoxLine.Core.Entities;

namespace BoxLine.Api.Contracts;

public sealed record CreateContainerRequest(
    [Required] string Number,
    int ContainerTypeId,
    OwnershipType OwnershipType,
    ContainerStatus Status = ContainerStatus.Available);

/// <summary>The container number is the container's identity and cannot be changed.</summary>
public sealed record UpdateContainerRequest(
    int ContainerTypeId,
    OwnershipType OwnershipType,
    ContainerStatus Status);

public sealed record ContainerResponse(
    int Id,
    string Number,
    int ContainerTypeId,
    string ContainerTypeCode,
    OwnershipType OwnershipType,
    ContainerStatus Status)
{
    public static ContainerResponse From(Container container) => new(
        container.Id,
        container.Number,
        container.ContainerTypeId,
        container.ContainerType?.Code ?? string.Empty,
        container.OwnershipType,
        container.Status);
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
