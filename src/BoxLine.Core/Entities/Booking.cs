namespace BoxLine.Core.Entities;

public enum BookingStatus
{
    Draft,
    Confirmed,
    Cancelled,
}

/// <summary>A shipper's request to move containers from a port of loading to a port of discharge.</summary>
public class Booking
{
    public int Id { get; set; }

    /// <summary>Generated reference such as BL-202611-0001 (month of creation plus a monthly sequence).</summary>
    public string BookingNumber { get; set; } = string.Empty;

    public int ShipperId { get; set; }

    public User? Shipper { get; set; }

    /// <summary>Port of loading.</summary>
    public int PolId { get; set; }

    public Port? Pol { get; set; }

    /// <summary>Port of discharge.</summary>
    public int PodId { get; set; }

    public Port? Pod { get; set; }

    /// <summary>The voyage carrying the booking. Assigned in a later phase; null until then.</summary>
    public int? VoyageId { get; set; }

    public string CargoDescription { get; set; } = string.Empty;

    public BookingStatus Status { get; set; } = BookingStatus.Draft;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Optimistic-concurrency token maintained by the database.</summary>
    public byte[] RowVersion { get; set; } = [];

    public List<BookingLine> Lines { get; set; } = [];
}

/// <summary>A quantity of one container type within a booking.</summary>
public class BookingLine
{
    public int Id { get; set; }

    public int BookingId { get; set; }

    public int ContainerTypeId { get; set; }

    public ContainerType? ContainerType { get; set; }

    public int Quantity { get; set; }
}
