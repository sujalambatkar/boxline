using System.Text.RegularExpressions;
using BoxLine.Core.Common;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;

namespace BoxLine.Core.Voyages;

public sealed record CreateVoyageCommand(int VesselId, string? VoyageNumber, IReadOnlyList<CallDraft> Calls);

public sealed record UpdateVoyageCommand(int VesselId, string? VoyageNumber, IReadOnlyList<CallDraft> Calls);

public sealed record RecordActualsCommand(DateTime? Ata, DateTime? Atd);

/// <summary>
/// Voyage management for operations staff (the caller must be Ops; the API enforces the role): creating
/// and editing rotations, recording actual arrival/departure times, and moving a voyage through its lifecycle.
/// </summary>
public sealed partial class VoyageService(
    IVoyageRepository voyages,
    IVesselRepository vessels,
    IPortRepository ports,
    IBookingRepository bookings,
    TimeProvider clock)
{
    public const int MaxVoyageNumberLength = 20;
    public const int MaxPageSize = 100;

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9 ./-]*$")]
    private static partial Regex VoyageNumberPattern();

    public async Task<Voyage> CreateAsync(CreateVoyageCommand command, CancellationToken cancellationToken)
    {
        var (number, calls) = await ValidateAsync(command.VesselId, command.VoyageNumber, command.Calls, excludeVoyageId: null, cancellationToken);

        var voyage = new Voyage
        {
            VesselId = command.VesselId,
            VoyageNumber = number,
            Status = VoyageStatus.Scheduled,
            Calls = calls,
        };
        await voyages.AddAsync(voyage, cancellationToken);

        return await GetAsync(voyage.Id, cancellationToken);
    }

    /// <summary>Replaces the vessel, number and rotation of a scheduled voyage, provided every active booking on it is still served.</summary>
    public async Task<Voyage> UpdateAsync(int id, UpdateVoyageCommand command, CancellationToken cancellationToken)
    {
        var voyage = await voyages.GetByIdAsync(id, cancellationToken) ?? throw NotFound(id);
        if (voyage.Status != VoyageStatus.Scheduled)
        {
            throw new ConflictException($"Only scheduled voyages can be edited; this voyage is {voyage.Status}.");
        }

        var (number, calls) = await ValidateAsync(command.VesselId, command.VoyageNumber, command.Calls, id, cancellationToken);

        var orphaned = (await bookings.ListByVoyageAsync(id, cancellationToken))
            .Where(b => b.Status != BookingStatus.Cancelled && VoyageRotation.FindLeg(calls, b.PolId, b.PodId) is null)
            .Select(b => b.BookingNumber)
            .ToList();
        if (orphaned.Count > 0)
        {
            throw new ConflictException(
                $"The new rotation would no longer serve {string.Join(", ", orphaned)}. Move those bookings to another voyage first.");
        }

        voyage.VesselId = command.VesselId;
        voyage.VoyageNumber = number;
        await voyages.ReplaceRotationAsync(voyage, calls, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Records the actual arrival and/or departure at one call. Allowed only while the voyage is underway, in
    /// rotation order: a call can only be updated once the previous port has departed, and a departure needs its arrival.
    /// </summary>
    public async Task<Voyage> RecordActualsAsync(int voyageId, int callId, RecordActualsCommand command, CancellationToken cancellationToken)
    {
        var voyage = await voyages.GetByIdAsync(voyageId, cancellationToken) ?? throw NotFound(voyageId);
        var call = voyage.Calls.FirstOrDefault(c => c.Id == callId)
            ?? throw new NotFoundException($"Call {callId} was not found on voyage {voyage.VoyageNumber}.");

        if (voyage.Status != VoyageStatus.Underway)
        {
            throw new ConflictException($"Actual times can only be recorded while a voyage is underway; this voyage is {voyage.Status}.");
        }

        if (command.Ata is null && command.Atd is null)
        {
            throw new DomainValidationException("Ata", "Enter an actual arrival, an actual departure, or both.");
        }

        var ata = command.Ata is { } a ? ToUtc(a) : call.Ata;
        var atd = command.Atd is { } d ? ToUtc(d) : call.Atd;
        var errors = new List<string>();
        var latest = clock.GetUtcNow().UtcDateTime + VoyageRotation.FutureTolerance;

        var ordered = voyage.Calls.OrderBy(c => c.Sequence).ToList();
        var index = ordered.IndexOf(call);
        var previous = index > 0 ? ordered[index - 1] : null;
        var isLast = index == ordered.Count - 1;

        if (ata > latest || atd > latest)
        {
            errors.Add("Actual times cannot be in the future.");
        }

        if (previous is not null && previous.Atd is null)
        {
            errors.Add($"Record the departure from the previous port (call {index}) first.");
        }

        if (previous?.Atd is { } previousDeparture && ata is { } arrival && arrival < previousDeparture)
        {
            errors.Add("Arrival cannot be before the previous port's departure.");
        }

        if (atd is not null && ata is null && previous is not null)
        {
            errors.Add("Record the arrival before the departure.");
        }

        if (ata is { } arrived && atd is { } departed && departed < arrived)
        {
            errors.Add("Departure cannot be before arrival.");
        }

        if (isLast && command.Atd is not null)
        {
            errors.Add("The last port call has no departure.");
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(new Dictionary<string, string[]> { ["Actuals"] = errors.Distinct().ToArray() });
        }

        call.Ata = ata;
        call.Atd = atd;
        await voyages.UpdateAsync(voyage, cancellationToken);

        return await GetAsync(voyageId, cancellationToken);
    }

    /// <summary>Moves the voyage along its lifecycle.</summary>
    public async Task<Voyage> ChangeStatusAsync(int id, VoyageStatus target, CancellationToken cancellationToken)
    {
        var voyage = await voyages.GetByIdAsync(id, cancellationToken) ?? throw NotFound(id);
        VoyageWorkflow.EnsureCanTransition(voyage.Status, target);

        if (target == VoyageStatus.Completed && voyage.Calls.OrderBy(c => c.Sequence).Last().Ata is null)
        {
            throw new ConflictException("Record the arrival at the last port before completing the voyage.");
        }

        if (target == VoyageStatus.Cancelled)
        {
            var active = (await bookings.ListByVoyageAsync(id, cancellationToken))
                .Where(b => b.Status != BookingStatus.Cancelled)
                .Select(b => b.BookingNumber)
                .ToList();
            if (active.Count > 0)
            {
                throw new ConflictException(
                    $"Voyage {voyage.VoyageNumber} still carries {string.Join(", ", active)}. Move or cancel those bookings first.");
            }
        }

        voyage.Status = target;
        await voyages.UpdateAsync(voyage, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes a voyage that never sailed (scheduled or cancelled) and has no bookings at all.</summary>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var voyage = await voyages.GetByIdAsync(id, cancellationToken) ?? throw NotFound(id);

        if (voyage.Status is VoyageStatus.Underway or VoyageStatus.Completed)
        {
            throw new ConflictException($"A {voyage.Status} voyage is part of the sailing history and cannot be deleted.");
        }

        if ((await bookings.ListByVoyageAsync(id, cancellationToken)).Count > 0)
        {
            throw new ConflictException($"Voyage {voyage.VoyageNumber} has bookings linked to it and cannot be deleted.");
        }

        await voyages.DeleteAsync(voyage, cancellationToken);
    }

    public async Task<Voyage> GetAsync(int id, CancellationToken cancellationToken) =>
        await voyages.GetReadOnlyAsync(id, cancellationToken) ?? throw NotFound(id);

    public Task<PagedResult<Voyage>> SearchAsync(VoyageQuery query, CancellationToken cancellationToken) =>
        voyages.SearchAsync(
            query with { Page = Math.Max(1, query.Page), PageSize = Math.Clamp(query.PageSize, 1, MaxPageSize) },
            cancellationToken);

    /// <summary>Number of active (not cancelled) bookings on a voyage.</summary>
    public async Task<int> CountActiveBookingsAsync(int voyageId, CancellationToken cancellationToken) =>
        (await bookings.ListByVoyageAsync(voyageId, cancellationToken)).Count(b => b.Status != BookingStatus.Cancelled);

    private async Task<(string Number, List<VoyageCall> Calls)> ValidateAsync(
        int vesselId, string? voyageNumber, IReadOnlyList<CallDraft> drafts, int? excludeVoyageId, CancellationToken cancellationToken)
    {
        var errors = VoyageRotation.ValidatePlan(drafts).ToDictionary(e => e.Key, e => e.Value.ToList());

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                errors[field] = list = [];
            }

            list.Add(message);
        }

        var number = (voyageNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (number.Length == 0)
        {
            Add("VoyageNumber", "Voyage number is required.");
        }
        else if (number.Length > MaxVoyageNumberLength || !VoyageNumberPattern().IsMatch(number))
        {
            Add("VoyageNumber", $"Voyage number must be at most {MaxVoyageNumberLength} characters: letters, digits, spaces, dots, hyphens or slashes.");
        }

        if (await vessels.GetByIdAsync(vesselId, cancellationToken) is null)
        {
            Add("VesselId", "Vessel does not exist.");
        }

        foreach (var portId in drafts.Select(d => d.PortId).Distinct())
        {
            if (await ports.GetByIdAsync(portId, cancellationToken) is null)
            {
                Add(VoyageRotation.ErrorKey, $"Port {portId} does not exist.");
            }
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }

        if (await voyages.NumberExistsAsync(number, excludeVoyageId, cancellationToken))
        {
            throw new ConflictException($"A voyage numbered {number} already exists.");
        }

        var calls = drafts
            .Select(d => new VoyageCall
            {
                Sequence = d.Sequence,
                PortId = d.PortId,
                Eta = d.Eta is { } eta ? ToUtc(eta) : null,
                Etd = d.Etd is { } etd ? ToUtc(etd) : null,
            })
            .ToList();

        return (number, calls);
    }

    private static NotFoundException NotFound(int id) => new($"Voyage {id} was not found.");

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
