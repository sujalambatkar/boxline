using BoxLine.Core.Common;
using BoxLine.Core.Detention;
using BoxLine.Core.Entities;
using FluentAssertions;

namespace BoxLine.Tests.Core;

/// <remarks>
/// Expected amounts come from an independent calculation (a closed-form formula, not a day-by-day loop).
/// Tariff used throughout: 5 free days, days 6-10 at 40, days 11-15 at 80, day 16 onwards at 120.
/// Calendar: 2026-11-02 is a Monday, 11-06 a Friday, 11-07 a Saturday, 11-08 a Sunday.
/// </remarks>
public class DetentionCalculatorTests
{
    private static readonly DateOnly Monday = new(2026, 11, 2);

    private static DetentionTariff Standard() => new()
    {
        Id = 1,
        FreeDays = 5,
        ValidFrom = new DateOnly(2026, 1, 1),
        Slabs =
        [
            new() { FromDay = 6, ToDay = 10, RatePerDayUsd = 40m },
            new() { FromDay = 11, ToDay = 15, RatePerDayUsd = 80m },
            new() { FromDay = 16, ToDay = null, RatePerDayUsd = 120m },
        ],
    };

    private static DetentionBreakdown Days(int days, bool weekends = true, DetentionTariff? tariff = null) =>
        DetentionCalculator.Calculate(Monday, Monday.AddDays(days - 1), tariff ?? Standard(), weekends);

    // ---- free days --------------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void ReturnWithinTheFreeDays_CostsNothing(int days)
    {
        var result = Days(days);

        result.TotalAmountUsd.Should().Be(0m);
        result.FreeDaysUsed.Should().Be(days);
        result.ChargeableDays.Should().Be(0);
        result.Slabs.Should().BeEmpty();
    }

    [Fact]
    public void ASameDayReturn_IsOneDay()
    {
        var result = DetentionCalculator.Calculate(Monday, Monday, Standard(), countWeekendsAsChargeable: true);

        result.TotalDays.Should().Be(1);
        result.CountedDays.Should().Be(1);
        result.FreeDaysUsed.Should().Be(1);
    }

    [Fact]
    public void ExactlyTheLastFreeDay_IsStillFree_AndTheNextDayIsTheFirstCharged()
    {
        Days(5).TotalAmountUsd.Should().Be(0m);

        var sixth = Days(6);
        sixth.ChargeableDays.Should().Be(1);
        sixth.TotalAmountUsd.Should().Be(40m);
        sixth.Slabs.Should().ContainSingle().Which.Should().Be(new SlabCharge(6, 10, 1, 40m, 40m));
    }

    [Fact]
    public void WithNoFreeDays_TheFirstDayIsCharged()
    {
        var tariff = new DetentionTariff
        {
            FreeDays = 0,
            ValidFrom = new DateOnly(2026, 1, 1),
            Slabs = [new() { FromDay = 1, ToDay = 3, RatePerDayUsd = 10m }, new() { FromDay = 4, ToDay = null, RatePerDayUsd = 25m }],
        };

        Days(1, tariff: tariff).TotalAmountUsd.Should().Be(10m);
        Days(3, tariff: tariff).TotalAmountUsd.Should().Be(30m);
        Days(4, tariff: tariff).TotalAmountUsd.Should().Be(55m);
        Days(10, tariff: tariff).TotalAmountUsd.Should().Be(205m);
        Days(10, tariff: tariff).FreeDaysUsed.Should().Be(0);
    }

    // ---- slab boundaries --------------------------------------------------------------------------

    [Theory]
    [InlineData(6, 40, 5, 1)]
    [InlineData(10, 200, 5, 5)]
    [InlineData(11, 280, 5, 6)]
    [InlineData(15, 600, 5, 10)]
    [InlineData(16, 720, 5, 11)]
    [InlineData(30, 2400, 5, 25)]
    public void Totals_AtAndAroundEverySlabBoundary(int days, decimal total, int freeUsed, int chargeable)
    {
        var result = Days(days);

        result.TotalAmountUsd.Should().Be(total);
        result.FreeDaysUsed.Should().Be(freeUsed);
        result.ChargeableDays.Should().Be(chargeable);
    }

    [Fact]
    public void TheBreakdown_ListsEachSlabReached_WithItsDaysRateAndAmount()
    {
        var result = Days(16);

        result.Slabs.Should().Equal(
            new SlabCharge(6, 10, 5, 40m, 200m),
            new SlabCharge(11, 15, 5, 80m, 400m),
            new SlabCharge(16, null, 1, 120m, 120m));
        result.TotalDays.Should().Be(16);
        result.CountedDays.Should().Be(16);
        result.WeekendDaysSkipped.Should().Be(0);
    }

    [Fact]
    public void OnlySlabsThatWereReached_AreListed()
    {
        Days(10).Slabs.Should().ContainSingle().Which.FromDay.Should().Be(6);
        Days(11).Slabs.Should().HaveCount(2);
    }

    [Fact]
    public void TheOpenEndedLastSlab_KeepsChargingForAnyLength()
    {
        var result = Days(100);

        result.Slabs.Last().Should().Be(new SlabCharge(16, null, 85, 120m, 10_200m));
        result.TotalAmountUsd.Should().Be(10_800m);
    }

    [Fact]
    public void SlabsAreAppliedByDayNumber_NotByTheOrderTheyAreStored()
    {
        var tariff = Standard();
        tariff.Slabs.Reverse();

        Days(16, tariff: tariff).TotalAmountUsd.Should().Be(720m);
    }

    [Fact]
    public void TheMoneyIsExact_ForRatesWithCents()
    {
        var tariff = new DetentionTariff
        {
            FreeDays = 1,
            ValidFrom = new DateOnly(2026, 1, 1),
            Slabs = [new() { FromDay = 2, ToDay = null, RatePerDayUsd = 33.33m }],
        };

        Days(4, tariff: tariff).TotalAmountUsd.Should().Be(99.99m);
    }

    // ---- weekends ---------------------------------------------------------------------------------

    [Fact]
    public void Weekends_CountLikeAnyOtherDay_WhenChargeable()
    {
        // Friday 11-06 to Monday 11-16 is 11 calendar days, 4 of them on a weekend.
        var result = DetentionCalculator.Calculate(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 16), Standard(), true);

        result.TotalDays.Should().Be(11);
        result.CountedDays.Should().Be(11);
        result.WeekendDaysSkipped.Should().Be(0);
        result.TotalAmountUsd.Should().Be(280m);
    }

    [Fact]
    public void Weekends_AreSkippedEntirelyWhenNotChargeable()
    {
        var result = DetentionCalculator.Calculate(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 16), Standard(), false);

        result.TotalDays.Should().Be(11);
        result.CountedDays.Should().Be(7);
        result.WeekendDaysSkipped.Should().Be(4);
        result.FreeDaysUsed.Should().Be(5);
        result.ChargeableDays.Should().Be(2);
        result.Slabs.Should().ContainSingle().Which.Should().Be(new SlabCharge(6, 10, 2, 40m, 80m));
        result.TotalAmountUsd.Should().Be(80m);
    }

    [Fact]
    public void SkippedWeekends_DoNotUseUpFreeDays()
    {
        // Monday to the Sunday three weeks later: 28 days, 20 of them weekdays.
        var result = DetentionCalculator.Calculate(Monday, new DateOnly(2026, 11, 29), Standard(), false);

        result.CountedDays.Should().Be(20);
        result.FreeDaysUsed.Should().Be(5);
        result.TotalAmountUsd.Should().Be(1200m);
        DetentionCalculator.Calculate(Monday, new DateOnly(2026, 11, 29), Standard(), true).TotalAmountUsd.Should().Be(2160m);
    }

    [Fact]
    public void AClockThatStartsOnAWeekend_BeginsCountingOnTheNextWeekday()
    {
        var saturdayToMonday = DetentionCalculator.Calculate(new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 9), Standard(), false);

        saturdayToMonday.TotalDays.Should().Be(3);
        saturdayToMonday.CountedDays.Should().Be(1);
        saturdayToMonday.WeekendDaysSkipped.Should().Be(2);
        saturdayToMonday.FreeDaysUsed.Should().Be(1);
    }

    [Fact]
    public void APeriodMadeOnlyOfWeekendDays_CountsNothing_WhenWeekendsAreSkipped()
    {
        var result = DetentionCalculator.Calculate(new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 8), Standard(), false);

        result.CountedDays.Should().Be(0);
        result.FreeDaysUsed.Should().Be(0);
        result.TotalAmountUsd.Should().Be(0m);
        result.Slabs.Should().BeEmpty();
    }

    // ---- errors -----------------------------------------------------------------------------------

    [Fact]
    public void AReturnBeforeTheOutDate_IsRejected()
    {
        var act = () => DetentionCalculator.Calculate(Monday, Monday.AddDays(-1), Standard(), true);

        act.Should().Throw<DomainValidationException>().Which.Errors["ReturnDate"].Single()
            .Should().Be("Return date cannot be before the out date.");
    }

    [Fact]
    public void ATariffThatIsNotValidOnTheOutDate_IsRejected()
    {
        var tariff = Standard();
        tariff.ValidFrom = new DateOnly(2026, 11, 3);

        var act = () => DetentionCalculator.Calculate(Monday, Monday.AddDays(10), tariff, true);

        act.Should().Throw<DomainValidationException>().Which.Errors["OutDate"].Single().Should().Be("The tariff is not valid on 2026-11-02.");
    }

    [Fact]
    public void TheValidityPeriod_IsInclusiveAtBothEnds_AndOptionalAtTheEnd()
    {
        var tariff = Standard();
        tariff.ValidFrom = Monday;
        tariff.ValidTo = Monday.AddDays(30);

        DetentionCalculator.Calculate(Monday, Monday.AddDays(2), tariff, true).Should().NotBeNull();
        DetentionCalculator.Calculate(Monday.AddDays(30), Monday.AddDays(32), tariff, true).Should().NotBeNull();
        var late = () => DetentionCalculator.Calculate(Monday.AddDays(31), Monday.AddDays(32), tariff, true);
        late.Should().Throw<DomainValidationException>();

        tariff.ValidTo = null;
        DetentionCalculator.Calculate(Monday.AddDays(4000), Monday.AddDays(4002), tariff, true).Should().NotBeNull();
    }

    [Fact]
    public void ValidityIsJudgedOnTheOutDate_NotTheReturnDate()
    {
        var tariff = Standard();
        tariff.ValidTo = Monday.AddDays(1);

        // The clock started while the tariff was valid and ran past its end date: still priced by that tariff.
        DetentionCalculator.Calculate(Monday, Monday.AddDays(20), tariff, true).TotalAmountUsd.Should().Be(1_320m);
    }

    // ---- cross-check against an independent formula ---------------------------------------------------

    /// <summary>
    /// The same price worked out with arithmetic instead of walking the days: count the days that count, then for each
    /// slab take how much of the 1..N range overlaps it.
    /// </summary>
    private static (decimal Total, int Counted, int Free) Oracle(DateOnly from, DateOnly to, DetentionTariff tariff, bool weekends)
    {
        var counted = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Count(i => weekends || from.AddDays(i).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));

        var total = tariff.Slabs.Sum(slab =>
        {
            var top = slab.ToDay is { } end ? Math.Min(counted, end) : counted;
            return Math.Max(0, top - slab.FromDay + 1) * slab.RatePerDayUsd;
        });

        return (total, counted, Math.Min(counted, tariff.FreeDays));
    }

    [Fact]
    public void Calculator_AgreesWithTheClosedFormOracle_AcrossManyRandomInputs()
    {
        var random = new Random(2026);
        for (var i = 0; i < 3_000; i++)
        {
            var freeDays = random.Next(0, 15);
            var firstEnd = freeDays + random.Next(1, 8);
            var secondEnd = firstEnd + random.Next(1, 10);
            var tariff = new DetentionTariff
            {
                FreeDays = freeDays,
                ValidFrom = new DateOnly(2020, 1, 1),
                Slabs =
                [
                    new() { FromDay = freeDays + 1, ToDay = firstEnd, RatePerDayUsd = random.Next(5, 90) + 0.5m },
                    new() { FromDay = firstEnd + 1, ToDay = secondEnd, RatePerDayUsd = random.Next(90, 190) },
                    new() { FromDay = secondEnd + 1, ToDay = null, RatePerDayUsd = random.Next(190, 400) + 0.25m },
                ],
            };
            var start = new DateOnly(2026, 1, 1).AddDays(random.Next(0, 700));
            var end = start.AddDays(random.Next(0, 120));
            var weekends = random.Next(2) == 0;

            var result = DetentionCalculator.Calculate(start, end, tariff, weekends);
            var expected = Oracle(start, end, tariff, weekends);

            result.TotalAmountUsd.Should().Be(expected.Total, $"case {i}: {start} to {end}, weekends {weekends}");
            result.CountedDays.Should().Be(expected.Counted);
            result.FreeDaysUsed.Should().Be(expected.Free);
            result.ChargeableDays.Should().Be(result.CountedDays - result.FreeDaysUsed);
            result.Slabs.Sum(s => s.Days).Should().Be(result.ChargeableDays);
            result.Slabs.Sum(s => s.AmountUsd).Should().Be(result.TotalAmountUsd);
            result.TotalDays.Should().Be(end.DayNumber - start.DayNumber + 1);
            result.WeekendDaysSkipped.Should().Be(result.TotalDays - result.CountedDays);
        }
    }
}
