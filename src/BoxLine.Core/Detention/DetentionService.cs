using BoxLine.Core.Common;
using BoxLine.Core.Containers;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;

namespace BoxLine.Core.Detention;

public sealed record SaveTariffCommand(int ContainerTypeId, int FreeDays, DateOnly ValidFrom, DateOnly? ValidTo, IReadOnlyList<SlabDraft> Slabs);

public enum DetentionEngine
{
    CSharp,
    Sql,
}

public sealed record CalculateCommand(
    int ContainerTypeId, DateOnly OutDate, DateOnly ReturnDate, bool CountWeekendsAsChargeable, DetentionEngine Engine = DetentionEngine.CSharp);

/// <summary>A priced detention period for one container type.</summary>
/// <param name="IsOngoing">True when the container has not been returned, so the period runs up to today.</param>
public sealed record DetentionQuote(
    ContainerType ContainerType,
    DetentionTariff Tariff,
    DateOnly OutDate,
    DateOnly ReturnDate,
    bool IsOngoing,
    bool CountWeekendsAsChargeable,
    MovementEventType? StartedBy,
    string? ContainerNumber,
    string? BookingNumber,
    DetentionBreakdown Breakdown);

/// <summary>Tariff administration and detention pricing.</summary>
public sealed class DetentionService(
    IDetentionTariffRepository tariffs,
    IContainerTypeRepository containerTypes,
    IContainerRepository containers,
    IMovementRepository movements,
    IDetentionSqlCalculator sqlCalculator,
    TimeProvider clock)
{
    // ---- tariff administration (the API restricts these to Ops) -----------------------------------

    public Task<IReadOnlyList<DetentionTariff>> ListTariffsAsync(int? containerTypeId, CancellationToken cancellationToken) =>
        tariffs.ListAsync(containerTypeId, cancellationToken);

    public async Task<DetentionTariff> GetTariffAsync(int id, CancellationToken cancellationToken) =>
        await tariffs.GetReadOnlyAsync(id, cancellationToken) ?? throw TariffNotFound(id);

    public async Task<DetentionTariff> CreateTariffAsync(SaveTariffCommand command, CancellationToken cancellationToken)
    {
        await ValidateAsync(command, excludeId: null, cancellationToken);

        var tariff = new DetentionTariff
        {
            ContainerTypeId = command.ContainerTypeId,
            FreeDays = command.FreeDays,
            ValidFrom = command.ValidFrom,
            ValidTo = command.ValidTo,
            Slabs = ToSlabs(command.Slabs),
        };
        await tariffs.AddAsync(tariff, cancellationToken);

        return await GetTariffAsync(tariff.Id, cancellationToken);
    }

    public async Task<DetentionTariff> UpdateTariffAsync(int id, SaveTariffCommand command, CancellationToken cancellationToken)
    {
        var tariff = await tariffs.GetByIdAsync(id, cancellationToken) ?? throw TariffNotFound(id);
        await ValidateAsync(command, id, cancellationToken);

        tariff.ContainerTypeId = command.ContainerTypeId;
        tariff.FreeDays = command.FreeDays;
        tariff.ValidFrom = command.ValidFrom;
        tariff.ValidTo = command.ValidTo;
        await tariffs.ReplaceSlabsAsync(tariff, ToSlabs(command.Slabs), cancellationToken);

        return await GetTariffAsync(id, cancellationToken);
    }

    public async Task DeleteTariffAsync(int id, CancellationToken cancellationToken)
    {
        var tariff = await tariffs.GetByIdAsync(id, cancellationToken) ?? throw TariffNotFound(id);
        await tariffs.DeleteAsync(tariff, cancellationToken);
    }

    // ---- pricing ----------------------------------------------------------------------------------

    /// <summary>Prices an arbitrary period for a container type using the tariff valid on the out date.</summary>
    public async Task<DetentionQuote> CalculateAsync(CalculateCommand command, CancellationToken cancellationToken)
    {
        var type = await containerTypes.GetByIdAsync(command.ContainerTypeId, cancellationToken)
            ?? throw new DomainValidationException("ContainerTypeId", "Container type does not exist.");

        if (command.ReturnDate < command.OutDate)
        {
            throw new DomainValidationException("ReturnDate", DetentionCalculator.ReturnBeforeOutMessage);
        }

        var tariff = await tariffs.FindValidAsync(type.Id, command.OutDate, cancellationToken)
            ?? throw NoTariff(type, command.OutDate);

        var breakdown = command.Engine == DetentionEngine.Sql
            ? await sqlCalculator.CalculateAsync(tariff.Id, command.OutDate, command.ReturnDate, command.CountWeekendsAsChargeable, cancellationToken)
            : DetentionCalculator.Calculate(command.OutDate, command.ReturnDate, tariff, command.CountWeekendsAsChargeable);

        return new DetentionQuote(
            type, tariff, command.OutDate, command.ReturnDate, false, command.CountWeekendsAsChargeable, null, null, null, breakdown);
    }

    /// <summary>
    /// Prices the container's current or latest detention period from its movement history: from gate out full
    /// (or delivery) to empty returned, or up to today when it has not come back. Ops can price any container;
    /// a shipper only their own bookings' containers.
    /// </summary>
    public async Task<DetentionQuote> CalculateForContainerAsync(
        Actor actor, string? containerNumber, string? bookingNumber, bool countWeekendsAsChargeable, CancellationToken cancellationToken)
    {
        var error = Iso6346Validator.Validate(containerNumber);
        if (error is not null)
        {
            throw new DomainValidationException("ContainerNumber", error);
        }

        var number = Iso6346Validator.Normalize(containerNumber);
        var container = await containers.GetByNumberAsync(number, cancellationToken)
            ?? throw new NotFoundException($"Container {number} was not found.");

        var history = await movements.ListForContainerAsync(container.Id, shipperId: null, cancellationToken);
        if (!actor.IsOps && !history.Any(e => e.Booking?.ShipperId == actor.UserId))
        {
            // A shipper with no movement on their own bookings gets the same answer as for a container that does not exist.
            throw new NotFoundException($"Container {number} was not found.");
        }

        var wantedBooking = string.IsNullOrWhiteSpace(bookingNumber) ? null : bookingNumber.Trim().ToUpperInvariant();
        var cycle = DetentionCycle.Find(history, e =>
            (actor.IsOps || e.Booking?.ShipperId == actor.UserId)
            && (wantedBooking is null || e.Booking?.BookingNumber == wantedBooking));

        if (cycle is null)
        {
            throw new ConflictException(
                wantedBooking is null
                    ? $"Container {number} has not been gated out full or delivered yet, so no detention clock has started."
                    : $"Container {number} has no gate out or delivery recorded for booking {wantedBooking}.");
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var ongoing = cycle.End is null;
        var returnDate = cycle.End ?? (today < cycle.Start ? cycle.Start : today);

        var type = container.ContainerType!;
        var tariff = await tariffs.FindValidAsync(type.Id, cycle.Start, cancellationToken) ?? throw NoTariff(type, cycle.Start);
        var breakdown = DetentionCalculator.Calculate(cycle.Start, returnDate, tariff, countWeekendsAsChargeable);

        return new DetentionQuote(
            type, tariff, cycle.Start, returnDate, ongoing, countWeekendsAsChargeable, cycle.StartedBy, number,
            cycle.StartEvent.Booking?.BookingNumber, breakdown);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private async Task ValidateAsync(SaveTariffCommand command, int? excludeId, CancellationToken cancellationToken)
    {
        var errors = DetentionTariffRules.Validate(command.FreeDays, command.ValidFrom, command.ValidTo, command.Slabs)
            .ToDictionary(e => e.Key, e => e.Value.ToList());

        if (await containerTypes.GetByIdAsync(command.ContainerTypeId, cancellationToken) is null)
        {
            errors["ContainerTypeId"] = ["Container type does not exist."];
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }

        if (await tariffs.OverlapsAsync(command.ContainerTypeId, command.ValidFrom, command.ValidTo, excludeId, cancellationToken))
        {
            throw new ConflictException("Another tariff for this container type is already valid during part of that period.");
        }
    }

    private static List<DetentionSlab> ToSlabs(IEnumerable<SlabDraft> drafts) =>
        drafts.OrderBy(s => s.FromDay)
            .Select(s => new DetentionSlab { FromDay = s.FromDay, ToDay = s.ToDay, RatePerDayUsd = s.RatePerDayUsd })
            .ToList();

    private static NotFoundException TariffNotFound(int id) => new($"Tariff {id} was not found.");

    private static NotFoundException NoTariff(ContainerType type, DateOnly date) =>
        new($"No detention tariff is valid for {type.Code} on {date:yyyy-MM-dd}.");
}
