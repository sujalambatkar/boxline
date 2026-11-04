using BoxLine.Core.Common;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Core.Voyages;

namespace BoxLine.Core.Bookings;

public sealed record CreateBookingCommand(
    int? ShipperId,
    int PolId,
    int PodId,
    string? CargoDescription,
    IReadOnlyList<BookingLineDraft> Lines,
    int? VoyageId = null);

/// <param name="VoyageId">The sailing the booking travels on; null leaves the booking without one.</param>
public sealed record UpdateBookingCommand(
    int PolId,
    int PodId,
    string? CargoDescription,
    IReadOnlyList<BookingLineDraft> Lines,
    byte[]? RowVersion,
    int? VoyageId = null);

/// <summary>
/// Booking use cases. Enforces who may see and change a booking (shippers only their own, Ops everything),
/// the status workflow and the content rules.
/// </summary>
public sealed class BookingService(
    IBookingRepository bookings,
    IPortRepository ports,
    IContainerTypeRepository containerTypes,
    IUserRepository users,
    IVoyageRepository voyages,
    TimeProvider clock)
{
    public const int MaxPageSize = 100;

    /// <summary>Creates a draft booking. Shippers book for themselves; Ops must name the shipper.</summary>
    public async Task<Booking> CreateAsync(Actor actor, CreateBookingCommand command, CancellationToken cancellationToken)
    {
        var shipperId = await ResolveShipperIdAsync(actor, command.ShipperId, cancellationToken);
        var draft = new BookingDraft(command.PolId, command.PodId, command.CargoDescription, command.Lines);
        await ValidateAsync(draft, command.VoyageId, currentVoyageId: null, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        var booking = new Booking
        {
            ShipperId = shipperId,
            PolId = command.PolId,
            PodId = command.PodId,
            VoyageId = command.VoyageId,
            CargoDescription = command.CargoDescription!.Trim(),
            Status = BookingStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            Lines = command.Lines.Select(l => new BookingLine { ContainerTypeId = l.ContainerTypeId, Quantity = l.Quantity }).ToList(),
        };

        await bookings.AddAsync(booking, cancellationToken);
        return await GetAsync(actor, booking.Id, cancellationToken);
    }

    /// <summary>Replaces the content of a draft booking.</summary>
    public async Task<Booking> UpdateAsync(Actor actor, int id, UpdateBookingCommand command, CancellationToken cancellationToken)
    {
        var booking = await LoadForActorAsync(actor, id, cancellationToken);
        if (booking.Status != BookingStatus.Draft)
        {
            throw new ConflictException($"Only draft bookings can be edited; this booking is {booking.Status}.");
        }

        await ValidateAsync(
            new BookingDraft(command.PolId, command.PodId, command.CargoDescription, command.Lines),
            command.VoyageId,
            booking.VoyageId,
            cancellationToken);

        booking.PolId = command.PolId;
        booking.PodId = command.PodId;
        booking.VoyageId = command.VoyageId;
        booking.CargoDescription = command.CargoDescription!.Trim();
        booking.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        booking.Lines.Clear();
        booking.Lines.AddRange(command.Lines.Select(l => new BookingLine { ContainerTypeId = l.ContainerTypeId, Quantity = l.Quantity }));

        await bookings.UpdateAsync(booking, command.RowVersion, cancellationToken);
        return await GetAsync(actor, id, cancellationToken);
    }

    /// <summary>
    /// Puts a booking on a sailing (or takes it off with null). Ops only; shippers choose a sailing when they
    /// create or edit a draft. Cancelled bookings cannot be moved.
    /// </summary>
    public async Task<Booking> AssignVoyageAsync(Actor actor, int id, int? voyageId, CancellationToken cancellationToken)
    {
        if (!actor.IsOps)
        {
            throw new ForbiddenException("Only operations staff can assign a booking to a voyage.");
        }

        var booking = await LoadForActorAsync(actor, id, cancellationToken);
        if (booking.Status == BookingStatus.Cancelled)
        {
            throw new ConflictException("A cancelled booking cannot be assigned to a voyage.");
        }

        if (voyageId is not null)
        {
            await EnsureVoyageServesAsync(voyageId.Value, booking.PolId, booking.PodId, booking.VoyageId, cancellationToken);
        }

        booking.VoyageId = voyageId;
        booking.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await bookings.UpdateAsync(booking, expectedRowVersion: null, cancellationToken);

        return await GetAsync(actor, id, cancellationToken);
    }

    public Task<Booking> ConfirmAsync(Actor actor, int id, CancellationToken cancellationToken) =>
        TransitionAsync(actor, id, BookingStatus.Confirmed, cancellationToken);

    public Task<Booking> CancelAsync(Actor actor, int id, CancellationToken cancellationToken) =>
        TransitionAsync(actor, id, BookingStatus.Cancelled, cancellationToken);

    /// <exception cref="NotFoundException">The booking does not exist or belongs to another shipper.</exception>
    public async Task<Booking> GetAsync(Actor actor, int id, CancellationToken cancellationToken) =>
        await LoadForActorAsync(actor, id, cancellationToken);

    /// <summary>Searches bookings. Shippers are always limited to their own, whatever filter they pass.</summary>
    public Task<PagedResult<Booking>> SearchAsync(Actor actor, BookingQuery query, CancellationToken cancellationToken)
    {
        var scoped = query with
        {
            ShipperId = actor.IsOps ? query.ShipperId : actor.UserId,
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, MaxPageSize),
        };

        return bookings.SearchAsync(scoped, cancellationToken);
    }

    private async Task<Booking> TransitionAsync(Actor actor, int id, BookingStatus target, CancellationToken cancellationToken)
    {
        var booking = await LoadForActorAsync(actor, id, cancellationToken);
        BookingWorkflow.EnsureCanTransition(booking.Status, target);

        var now = clock.GetUtcNow().UtcDateTime;
        booking.Status = target;
        booking.UpdatedAt = now;
        if (target == BookingStatus.Confirmed)
        {
            booking.ConfirmedAt = now;
        }

        await bookings.UpdateAsync(booking, expectedRowVersion: null, cancellationToken);

        return booking;
    }

    private async Task<Booking> LoadForActorAsync(Actor actor, int id, CancellationToken cancellationToken)
    {
        var booking = await bookings.GetByIdAsync(id, cancellationToken);

        // A shipper asking for someone else's booking gets the same answer as for a missing one.
        if (booking is null || (!actor.IsOps && booking.ShipperId != actor.UserId))
        {
            throw new NotFoundException($"Booking {id} was not found.");
        }

        return booking;
    }

    private async Task<int> ResolveShipperIdAsync(Actor actor, int? requestedShipperId, CancellationToken cancellationToken)
    {
        if (!actor.IsOps)
        {
            return actor.UserId;
        }

        if (requestedShipperId is null)
        {
            throw new DomainValidationException("ShipperId", "Choose the shipper this booking is for.");
        }

        var shipper = await users.GetByIdAsync(requestedShipperId.Value, cancellationToken);
        if (shipper is null || shipper.Role != UserRole.Shipper)
        {
            throw new DomainValidationException("ShipperId", "The selected shipper does not exist.");
        }

        return shipper.Id;
    }

    private async Task ValidateAsync(BookingDraft draft, int? voyageId, int? currentVoyageId, CancellationToken cancellationToken)
    {
        var errors = BookingRules.Validate(draft).ToDictionary(e => e.Key, e => e.Value.ToList());

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                errors[field] = list = [];
            }

            list.Add(message);
        }

        var pol = await ports.GetByIdAsync(draft.PolId, cancellationToken);
        if (pol is null)
        {
            Add("PolId", "Port of loading does not exist.");
        }

        var pod = draft.PodId == draft.PolId ? pol : await ports.GetByIdAsync(draft.PodId, cancellationToken);
        if (pod is null && draft.PodId != draft.PolId)
        {
            Add("PodId", "Port of discharge does not exist.");
        }

        var knownTypes = (await containerTypes.ListAsync(cancellationToken)).Select(t => t.Id).ToHashSet();
        if (draft.Lines.Any(l => !knownTypes.Contains(l.ContainerTypeId)))
        {
            Add("Lines", "One or more container types do not exist.");
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }

        if (voyageId is not null)
        {
            await EnsureVoyageServesAsync(voyageId.Value, draft.PolId, draft.PodId, currentVoyageId, cancellationToken);
        }
    }

    /// <summary>
    /// The voyage must exist, sail from the loading port to the discharge port in that order and, unless the
    /// booking is already on it, still be open for bookings from that port.
    /// </summary>
    private async Task EnsureVoyageServesAsync(int voyageId, int polId, int podId, int? currentVoyageId, CancellationToken cancellationToken)
    {
        var voyage = await voyages.GetReadOnlyAsync(voyageId, cancellationToken)
            ?? throw new DomainValidationException("VoyageId", "Voyage does not exist.");

        var pol = await ports.GetByIdAsync(polId, cancellationToken);
        var pod = await ports.GetByIdAsync(podId, cancellationToken);
        VoyageRotation.EnsureServes(voyage, pol!, pod!);

        if (voyageId != currentVoyageId && !VoyageRotation.IsOpenForBooking(voyage, polId))
        {
            throw new DomainValidationException(
                "VoyageId", $"Voyage {voyage.VoyageNumber} is {voyage.Status.ToString().ToLowerInvariant()} and is not taking bookings from {pol!.UnLocode}.");
        }
    }
}
