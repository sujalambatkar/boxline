using System.ComponentModel.DataAnnotations;
using BoxLine.Core.Bookings;
using BoxLine.Core.Entities;

namespace BoxLine.Api.Contracts;

public sealed record BookingLineRequest(int ContainerTypeId, int Quantity);

/// <param name="ShipperId">Required when Ops books on behalf of a shipper; ignored for shippers.</param>
public sealed record CreateBookingRequest(
    int PolId,
    int PodId,
    [Required] string CargoDescription,
    [Required] List<BookingLineRequest> Lines,
    int? ShipperId = null);

/// <param name="RowVersion">The version returned when the booking was loaded; a stale value is rejected with 409.</param>
public sealed record UpdateBookingRequest(
    int PolId,
    int PodId,
    [Required] string CargoDescription,
    [Required] List<BookingLineRequest> Lines,
    string? RowVersion = null);

public sealed record PortSummary(int Id, string UnLocode, string Name)
{
    public static PortSummary From(Port? port) => port is null ? new(0, string.Empty, string.Empty) : new(port.Id, port.UnLocode, port.Name);
}

public sealed record BookingLineResponse(int Id, int ContainerTypeId, string ContainerTypeCode, int Quantity);

public sealed record BookingResponse(
    int Id,
    string BookingNumber,
    int ShipperId,
    string ShipperName,
    PortSummary Pol,
    PortSummary Pod,
    int? VoyageId,
    string CargoDescription,
    BookingStatus Status,
    IReadOnlyList<BookingStatus> AllowedTransitions,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string RowVersion,
    int TotalContainers,
    IReadOnlyList<BookingLineResponse> Lines)
{
    public static BookingResponse From(Booking booking) => new(
        booking.Id,
        booking.BookingNumber,
        booking.ShipperId,
        booking.Shipper?.ShipperCompany ?? booking.Shipper?.Email ?? string.Empty,
        PortSummary.From(booking.Pol),
        PortSummary.From(booking.Pod),
        booking.VoyageId,
        booking.CargoDescription,
        booking.Status,
        BookingWorkflow.NextStatuses(booking.Status),
        DateTime.SpecifyKind(booking.CreatedAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(booking.UpdatedAt, DateTimeKind.Utc),
        Convert.ToBase64String(booking.RowVersion),
        booking.Lines.Sum(l => l.Quantity),
        booking.Lines
            .OrderBy(l => l.ContainerType?.Code)
            .Select(l => new BookingLineResponse(l.Id, l.ContainerTypeId, l.ContainerType?.Code ?? string.Empty, l.Quantity))
            .ToList());
}
