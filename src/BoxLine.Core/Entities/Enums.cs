namespace BoxLine.Core.Entities;

/// <summary>Whether BoxLine owns a container or leases it from another lessor.</summary>
public enum OwnershipType
{
    Owned,
    Leased,
}

/// <summary>Operational state of a container in the fleet master.</summary>
public enum ContainerStatus
{
    Available,
    InUse,
    UnderRepair,
}
