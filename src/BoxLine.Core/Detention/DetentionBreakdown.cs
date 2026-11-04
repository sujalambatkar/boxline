namespace BoxLine.Core.Detention;

/// <summary>The charge for the days that fell into one slab.</summary>
public sealed record SlabCharge(int FromDay, int? ToDay, int Days, decimal RatePerDayUsd, decimal AmountUsd);

/// <summary>The result of a detention calculation.</summary>
/// <param name="TotalDays">Calendar days from the out date to the return date, both included.</param>
/// <param name="CountedDays">Days that count towards free days and charges (all days, or weekdays only).</param>
/// <param name="WeekendDaysSkipped">Saturdays and Sundays left out when weekends are not chargeable.</param>
/// <param name="FreeDaysUsed">Counted days that fell inside the free days.</param>
/// <param name="ChargeableDays">Counted days beyond the free days.</param>
/// <param name="Slabs">Only the slabs that were actually reached, lowest first.</param>
public sealed record DetentionBreakdown(
    int TotalDays,
    int CountedDays,
    int WeekendDaysSkipped,
    int FreeDaysUsed,
    int ChargeableDays,
    IReadOnlyList<SlabCharge> Slabs,
    decimal TotalAmountUsd);
