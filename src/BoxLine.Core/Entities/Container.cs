namespace BoxLine.Core.Entities;

/// <summary>A physical container identified by its ISO 6346 number.</summary>
public class Container
{
    public int Id { get; set; }

    /// <summary>Normalized ISO 6346 number, e.g. CSQU3054383. Immutable once created.</summary>
    public string Number { get; set; } = string.Empty;

    public int ContainerTypeId { get; set; }

    public ContainerType? ContainerType { get; set; }

    public OwnershipType OwnershipType { get; set; }

    public ContainerStatus Status { get; set; } = ContainerStatus.Available;
}
