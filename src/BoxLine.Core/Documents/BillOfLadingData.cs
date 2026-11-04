using BoxLine.Core.Entities;
using BoxLine.Core.Voyages;

namespace BoxLine.Core.Documents;

public sealed record BolPort(string UnLocode, string Name);

public sealed record BolVoyage(string VoyageNumber, string VesselName, string ImoNumber, DateTime? Departure, DateTime? Arrival);

/// <summary>A container type and how many of it were booked.</summary>
public sealed record BolBookedContainers(string TypeCode, string Description, int Quantity);

/// <summary>A specific container recorded against the booking.</summary>
public sealed record BolLoadedContainer(string Number, string TypeCode);

/// <summary>Everything printed on a bill of lading. Only facts the system holds are included.</summary>
/// <param name="IssueDate">The day the booking was confirmed; null while it is a draft.</param>
/// <param name="IsDraft">True when the document must carry the DRAFT watermark (any booking that is not confirmed).</param>
public sealed record BillOfLadingData(
    string BookingNumber,
    BookingStatus Status,
    bool IsDraft,
    DateOnly? IssueDate,
    string ShipperName,
    string ShipperEmail,
    BolPort PortOfLoading,
    BolPort PortOfDischarge,
    BolVoyage? Voyage,
    string CargoDescription,
    IReadOnlyList<BolBookedContainers> Booked,
    IReadOnlyList<BolLoadedContainer> Loaded,
    DateTime GeneratedAt)
{
    public int TotalBooked => Booked.Sum(b => b.Quantity);

    /// <summary>Builds the document data from a booking (with its relations loaded) and its movements.</summary>
    /// <param name="movements">The booking's movement events, each with its container and type loaded.</param>
    public static BillOfLadingData From(Booking booking, IEnumerable<ContainerMovement> movements, DateTime nowUtc)
    {
        var confirmed = booking.Status == BookingStatus.Confirmed;

        // Bookings confirmed before the date was recorded fall back to their last update.
        DateOnly? issue = confirmed ? DateOnly.FromDateTime(booking.ConfirmedAt ?? booking.UpdatedAt) : null;

        BolVoyage? voyage = booking.Voyage is { } v
            ? new BolVoyage(
                v.VoyageNumber,
                v.Vessel?.Name ?? string.Empty,
                v.Vessel?.ImoNumber ?? string.Empty,
                VoyageRotation.Departure(v.Calls, booking.PolId),
                VoyageRotation.Arrival(v.Calls, booking.PodId))
            : null;

        var loaded = movements
            .Where(m => m.Container is not null)
            .Select(m => m.Container!)
            .DistinctBy(c => c.Id)
            .OrderBy(c => c.Number, StringComparer.Ordinal)
            .Select(c => new BolLoadedContainer(c.Number, c.ContainerType?.Code ?? string.Empty))
            .ToList();

        return new BillOfLadingData(
            booking.BookingNumber,
            booking.Status,
            IsDraft: !confirmed,
            issue,
            booking.Shipper?.ShipperCompany ?? booking.Shipper?.Email ?? string.Empty,
            booking.Shipper?.Email ?? string.Empty,
            new BolPort(booking.Pol?.UnLocode ?? string.Empty, booking.Pol?.Name ?? string.Empty),
            new BolPort(booking.Pod?.UnLocode ?? string.Empty, booking.Pod?.Name ?? string.Empty),
            voyage,
            booking.CargoDescription,
            booking.Lines
                .OrderBy(l => l.ContainerType?.LengthFt).ThenBy(l => l.ContainerType?.Code, StringComparer.Ordinal)
                .Select(l => new BolBookedContainers(l.ContainerType?.Code ?? string.Empty, l.ContainerType?.Description ?? string.Empty, l.Quantity))
                .ToList(),
            loaded,
            nowUtc);
    }
}
