namespace BoxLine.Core.Entities;

/// <summary>The stages of a container's journey, in the order they normally happen.</summary>
public enum MovementEventType
{
    GateOutEmpty,
    Stuffed,
    GateInFull,
    LoadedOnVessel,
    Discharged,
    GateOutFull,
    DeliveredToConsignee,
    EmptyReturned,
}

/// <summary>One recorded event in a container's life.</summary>
public class ContainerMovement
{
    public int Id { get; set; }

    public int ContainerId { get; set; }

    public Container? Container { get; set; }

    /// <summary>The booking this movement belongs to; null for empty repositioning outside any booking.</summary>
    public int? BookingId { get; set; }

    public Booking? Booking { get; set; }

    public MovementEventType EventType { get; set; }

    /// <summary>UN/LOCODE of the place where the event happened (a port or an inland depot).</summary>
    public string LocationUnLocode { get; set; } = string.Empty;

    /// <summary>When the event happened (UTC), as reported by operations.</summary>
    public DateTime OccurredAt { get; set; }

    public string? Remarks { get; set; }

    public int RecordedByUserId { get; set; }

    /// <summary>When the event was entered into the system (UTC).</summary>
    public DateTime RecordedAt { get; set; }
}
