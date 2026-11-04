namespace BoxLine.Core.Entities;

public enum VoyageStatus
{
    Scheduled,
    Underway,
    Completed,
    Cancelled,
}

/// <summary>One scheduled sailing of a vessel through a rotation of ports.</summary>
public class Voyage
{
    public int Id { get; set; }

    public int VesselId { get; set; }

    public Vessel? Vessel { get; set; }

    /// <summary>Upper-case reference such as BXL2611E; unique.</summary>
    public string VoyageNumber { get; set; } = string.Empty;

    public VoyageStatus Status { get; set; } = VoyageStatus.Scheduled;

    /// <summary>The port rotation, ordered by <see cref="VoyageCall.Sequence"/>.</summary>
    public List<VoyageCall> Calls { get; set; } = [];
}

/// <summary>A planned stop of a voyage at a port, with estimated and actual arrival/departure times (UTC).</summary>
public class VoyageCall
{
    public int Id { get; set; }

    public int VoyageId { get; set; }

    public int PortId { get; set; }

    public Port? Port { get; set; }

    /// <summary>Position in the rotation; strictly increasing along the voyage.</summary>
    public int Sequence { get; set; }

    public DateTime? Eta { get; set; }

    public DateTime? Etd { get; set; }

    public DateTime? Ata { get; set; }

    public DateTime? Atd { get; set; }
}
