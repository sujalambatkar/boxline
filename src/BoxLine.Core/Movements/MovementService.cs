using BoxLine.Core.Common;
using BoxLine.Core.Containers;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Core.Ports;

namespace BoxLine.Core.Movements;

public sealed record RecordMovementCommand(
    string? ContainerNumber,
    MovementEventType EventType,
    string? LocationUnLocode,
    DateTime OccurredAt,
    string? BookingNumber,
    string? Remarks);

/// <summary>A container with its visible history. <see cref="AllowedNextEvents"/> is filled for Ops only.</summary>
public sealed record ContainerTracking(
    Container Container,
    IReadOnlyList<ContainerMovement> Events,
    IReadOnlyList<MovementEventType> AllowedNextEvents);

public sealed record BookingTracking(Booking Booking, IReadOnlyList<ContainerTracking> Containers);

/// <summary>Recording container movements and reading them back, with shippers limited to their own bookings.</summary>
public sealed class MovementService(
    IMovementRepository movements,
    IContainerRepository containers,
    IBookingRepository bookings,
    TimeProvider clock)
{
    public const int MaxRemarksLength = 500;

    /// <summary>Records an event for a container. Ops only.</summary>
    public async Task<ContainerMovement> RecordAsync(Actor actor, RecordMovementCommand command, CancellationToken cancellationToken)
    {
        if (!actor.IsOps)
        {
            throw new ForbiddenException("Only operations staff can record container movements.");
        }

        var container = await FindContainerAsync(command.ContainerNumber, cancellationToken)
            ?? throw new NotFoundException($"Container {Iso6346Validator.Normalize(command.ContainerNumber)} was not found.");

        var errors = new Dictionary<string, string[]>();
        var location = UnLocode.Normalize(command.LocationUnLocode);
        if (!UnLocode.IsValid(location))
        {
            errors["LocationUnLocode"] = ["Location must be a UN/LOCODE: two letters followed by three letters or digits 2-9, e.g. INNSA."];
        }

        var remarks = string.IsNullOrWhiteSpace(command.Remarks) ? null : command.Remarks.Trim();
        if (remarks is { Length: > MaxRemarksLength })
        {
            errors["Remarks"] = [$"Remarks must be at most {MaxRemarksLength} characters."];
        }

        int? bookingId = null;
        if (!string.IsNullOrWhiteSpace(command.BookingNumber))
        {
            var booking = await bookings.GetByNumberAsync(command.BookingNumber.Trim().ToUpperInvariant(), cancellationToken);
            if (booking is null)
            {
                errors["BookingNumber"] = ["Booking does not exist."];
            }
            else if (booking.Status != BookingStatus.Confirmed)
            {
                errors["BookingNumber"] = [$"Movements can only be recorded against a confirmed booking; {booking.BookingNumber} is {booking.Status}."];
            }
            else
            {
                bookingId = booking.Id;
            }
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        var occurredAt = ToUtc(command.OccurredAt);
        var now = clock.GetUtcNow().UtcDateTime;

        return await movements.RecordAsync(container.Id, last =>
        {
            MovementStateMachine.Validate(last is null ? null : new LastMovement(last.EventType, last.OccurredAt), command.EventType, occurredAt, now);

            return new ContainerMovement
            {
                ContainerId = container.Id,
                BookingId = bookingId,
                EventType = command.EventType,
                LocationUnLocode = location,
                OccurredAt = occurredAt,
                Remarks = remarks,
                RecordedByUserId = actor.UserId,
                RecordedAt = now,
            };
        }, cancellationToken);
    }

    /// <summary>
    /// A container's timeline. Shippers only see events that belong to their own bookings; if there are none,
    /// the container is reported as not found, exactly as if it did not exist.
    /// </summary>
    public async Task<ContainerTracking> GetContainerTrackingAsync(Actor actor, string? containerNumber, CancellationToken cancellationToken)
    {
        var container = await FindContainerAsync(containerNumber, cancellationToken)
            ?? throw new NotFoundException($"Container {Iso6346Validator.Normalize(containerNumber)} was not found.");

        var events = await movements.ListForContainerAsync(container.Id, actor.IsOps ? null : actor.UserId, cancellationToken);
        if (!actor.IsOps && events.Count == 0)
        {
            throw new NotFoundException($"Container {container.Number} was not found.");
        }

        var allowed = actor.IsOps ? MovementStateMachine.AllowedNext(events.LastOrDefault()?.EventType) : [];
        return new ContainerTracking(container, events, allowed);
    }

    /// <summary>The containers moving under a booking, each with its events. Shippers can only look up their own bookings.</summary>
    public async Task<BookingTracking> GetBookingTrackingAsync(Actor actor, string? bookingNumber, CancellationToken cancellationToken)
    {
        var number = (bookingNumber ?? string.Empty).Trim().ToUpperInvariant();
        var booking = await bookings.GetByNumberAsync(number, cancellationToken);

        if (booking is null || (!actor.IsOps && booking.ShipperId != actor.UserId))
        {
            throw new NotFoundException($"Booking {number} was not found.");
        }

        var events = await movements.ListForBookingAsync(booking.Id, cancellationToken);
        var perContainer = events
            .GroupBy(e => e.ContainerId)
            .Select(g => new ContainerTracking(g.First().Container!, g.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToList(), []))
            .OrderBy(c => c.Container.Number, StringComparer.Ordinal)
            .ToList();

        return new BookingTracking(booking, perContainer);
    }

    private async Task<Container?> FindContainerAsync(string? number, CancellationToken cancellationToken)
    {
        var error = Iso6346Validator.Validate(number);
        if (error is not null)
        {
            throw new DomainValidationException("ContainerNumber", error);
        }

        return await containers.GetByNumberAsync(Iso6346Validator.Normalize(number), cancellationToken);
    }

    /// <summary>Treats unspecified times as UTC and converts local ones, so stored times are always UTC.</summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
