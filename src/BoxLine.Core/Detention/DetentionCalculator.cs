using BoxLine.Core.Common;
using BoxLine.Core.Entities;

namespace BoxLine.Core.Detention;

/// <summary>
/// Calculates detention charges. The same calculation exists as the stored procedure <c>usp_CalculateDetention</c>;
/// integration tests prove the two agree, so keep their rules identical.
/// </summary>
/// <remarks>
/// Days are calendar days, both the out date and the return date included, so a same-day return is one day.
/// Day numbers start at 1. The first <c>FreeDays</c> counted days are free; every later counted day is charged at
/// the rate of the slab containing its day number. When weekends are not chargeable, Saturdays and Sundays are
/// skipped completely: they neither use up free days nor cost anything, and day numbering only counts the others.
/// </remarks>
public static class DetentionCalculator
{
    public const string ReturnBeforeOutMessage = "Return date cannot be before the out date.";

    /// <summary>The message for a tariff that does not apply on the date the clock started.</summary>
    public static string TariffNotValidMessage(DateOnly outDate) => $"The tariff is not valid on {outDate:yyyy-MM-dd}.";

    /// <param name="outDate">The day the container left the terminal, which is when the clock starts.</param>
    /// <param name="returnDate">The day the empty container came back (or the day up to which to calculate).</param>
    /// <param name="countWeekendsAsChargeable">True counts every calendar day; false skips Saturdays and Sundays.</param>
    /// <exception cref="DomainValidationException">The return is before the out date, or the tariff was not valid on the out date.</exception>
    public static DetentionBreakdown Calculate(DateOnly outDate, DateOnly returnDate, DetentionTariff tariff, bool countWeekendsAsChargeable)
    {
        if (returnDate < outDate)
        {
            throw new DomainValidationException("ReturnDate", ReturnBeforeOutMessage);
        }

        if (!tariff.IsValidOn(outDate))
        {
            throw new DomainValidationException("OutDate", TariffNotValidMessage(outDate));
        }

        var slabs = tariff.Slabs.OrderBy(s => s.FromDay).ToList();
        var daysPerSlab = new int[slabs.Count];
        var totalDays = returnDate.DayNumber - outDate.DayNumber + 1;
        var counted = 0;
        var freeUsed = 0;

        for (var date = outDate; date <= returnDate; date = date.AddDays(1))
        {
            if (!countWeekendsAsChargeable && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            counted++;
            if (counted <= tariff.FreeDays)
            {
                freeUsed++;
                continue;
            }

            var index = slabs.FindIndex(s => counted >= s.FromDay && (s.ToDay is null || counted <= s.ToDay));
            if (index >= 0)
            {
                daysPerSlab[index]++;
            }
        }

        var charges = slabs
            .Select((slab, i) => new SlabCharge(slab.FromDay, slab.ToDay, daysPerSlab[i], slab.RatePerDayUsd, daysPerSlab[i] * slab.RatePerDayUsd))
            .Where(c => c.Days > 0)
            .ToList();

        return new DetentionBreakdown(
            totalDays,
            counted,
            totalDays - counted,
            freeUsed,
            counted - freeUsed,
            charges,
            charges.Sum(c => c.AmountUsd));
    }
}
