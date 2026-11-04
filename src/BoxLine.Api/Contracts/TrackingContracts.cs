using System.ComponentModel.DataAnnotations;
using BoxLine.Core.Entities;
using BoxLine.Core.Movements;

namespace BoxLine.Api.Contracts;

/// <param name="OccurredAt">When the event happened. A time with an offset or "Z" is converted to UTC; one without is taken as UTC.</param>
/// <param name="BookingNumber">Optional booking the movement belongs to; it must be confirmed.</param>
public sealed record RecordMovementRequest(
    [Required] string ContainerNumber,
    MovementEventType EventType,
    [Required] string LocationUnLocode,
    DateTime OccurredAt,
    string? BookingNumber = null,
    string? Remarks = null);

public sealed record MovementResponse(
    int Id,
    string ContainerNumber,
    MovementEventType EventType,
    string LocationUnLocode,
    DateTime OccurredAt,
    string? BookingNumber,
    string? Remarks)
{
    public static MovementResponse From(ContainerMovement movement, string containerNumber) => new(
        movement.Id,
        containerNumber,
        movement.EventType,
        movement.LocationUnLocode,
        DateTime.SpecifyKind(movement.OccurredAt, DateTimeKind.Utc),
        movement.Booking?.BookingNumber,
        movement.Remarks);
}

public sealed record ContainerTrackingResponse(
    string ContainerNumber,
    string ContainerType,
    MovementResponse? Current,
    IReadOnlyList<MovementResponse> Events,
    IReadOnlyList<MovementEventType> AllowedNextEvents)
{
    public static ContainerTrackingResponse From(ContainerTracking tracking)
    {
        var number = tracking.Container.Number;
        var events = tracking.Events.Select(e => MovementResponse.From(e, number)).ToList();
        return new ContainerTrackingResponse(
            number,
            tracking.Container.ContainerType?.Code ?? string.Empty,
            events.LastOrDefault(),
            events,
            tracking.AllowedNextEvents);
    }
}

public sealed record BookingTrackingResponse(
    string BookingNumber,
    BookingStatus Status,
    PortSummary Pol,
    PortSummary Pod,
    IReadOnlyList<ContainerTrackingResponse> Containers)
{
    public static BookingTrackingResponse From(BookingTracking tracking) => new(
        tracking.Booking.BookingNumber,
        tracking.Booking.Status,
        PortSummary.From(tracking.Booking.Pol),
        PortSummary.From(tracking.Booking.Pod),
        tracking.Containers.Select(ContainerTrackingResponse.From).ToList());
}
