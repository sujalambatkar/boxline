using System.ComponentModel.DataAnnotations;
using BoxLine.Core.Detention;
using BoxLine.Core.Entities;

namespace BoxLine.Api.Contracts;

/// <param name="ToDay">Last day of the slab; null for the open-ended last slab.</param>
public sealed record SlabRequest(int FromDay, int? ToDay, decimal RatePerDayUsd);

/// <param name="ValidTo">Last day (inclusive) the tariff applies; omit for no end date.</param>
public sealed record TariffRequest(
    int ContainerTypeId,
    int FreeDays,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    [Required] List<SlabRequest> Slabs);

public sealed record SlabResponse(int FromDay, int? ToDay, decimal RatePerDayUsd)
{
    public static SlabResponse From(DetentionSlab slab) => new(slab.FromDay, slab.ToDay, slab.RatePerDayUsd);
}

public sealed record TariffResponse(
    int Id,
    int ContainerTypeId,
    string ContainerType,
    int FreeDays,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    IReadOnlyList<SlabResponse> Slabs)
{
    public static TariffResponse From(DetentionTariff tariff) => new(
        tariff.Id,
        tariff.ContainerTypeId,
        tariff.ContainerType?.Code ?? string.Empty,
        tariff.FreeDays,
        tariff.ValidFrom,
        tariff.ValidTo,
        tariff.Slabs.OrderBy(s => s.FromDay).Select(SlabResponse.From).ToList());
}

/// <param name="Engine">"csharp" (default) or "sql" to run the same calculation in the stored procedure.</param>
public sealed record CalculateRequest(
    int ContainerTypeId,
    DateOnly OutDate,
    DateOnly ReturnDate,
    bool CountWeekendsAsChargeable = true,
    DetentionEngine Engine = DetentionEngine.CSharp);

public sealed record SlabChargeResponse(int FromDay, int? ToDay, int Days, decimal RatePerDayUsd, decimal AmountUsd)
{
    public static SlabChargeResponse From(SlabCharge charge) =>
        new(charge.FromDay, charge.ToDay, charge.Days, charge.RatePerDayUsd, charge.AmountUsd);
}

public sealed record DetentionResponse(
    string ContainerType,
    string? ContainerNumber,
    string? BookingNumber,
    MovementEventType? StartedBy,
    TariffResponse Tariff,
    DateOnly OutDate,
    DateOnly ReturnDate,
    bool IsOngoing,
    bool CountWeekendsAsChargeable,
    int TotalDays,
    int CountedDays,
    int WeekendDaysSkipped,
    int FreeDaysUsed,
    int ChargeableDays,
    IReadOnlyList<SlabChargeResponse> Slabs,
    decimal TotalAmountUsd,
    string Currency)
{
    public static DetentionResponse From(DetentionQuote quote) => new(
        quote.ContainerType.Code,
        quote.ContainerNumber,
        quote.BookingNumber,
        quote.StartedBy,
        TariffResponse.From(quote.Tariff),
        quote.OutDate,
        quote.ReturnDate,
        quote.IsOngoing,
        quote.CountWeekendsAsChargeable,
        quote.Breakdown.TotalDays,
        quote.Breakdown.CountedDays,
        quote.Breakdown.WeekendDaysSkipped,
        quote.Breakdown.FreeDaysUsed,
        quote.Breakdown.ChargeableDays,
        quote.Breakdown.Slabs.Select(SlabChargeResponse.From).ToList(),
        quote.Breakdown.TotalAmountUsd,
        "USD");
}
