using BoxLine.Core.Detention;
using FluentAssertions;

namespace BoxLine.Tests.Core;

public class DetentionTariffRulesTests
{
    private static readonly DateOnly From = new(2026, 1, 1);

    private static List<SlabDraft> Valid() => [new(6, 10, 40m), new(11, 15, 80m), new(16, null, 120m)];

    private static string[] Problems(int free, DateOnly? to, IReadOnlyList<SlabDraft> slabs, string key = "Slabs") =>
        DetentionTariffRules.Validate(free, From, to, slabs).GetValueOrDefault(key) ?? [];

    [Fact]
    public void Validate_AcceptsAWellFormedTariff() => DetentionTariffRules.Validate(5, From, null, Valid()).Should().BeEmpty();

    [Fact]
    public void Validate_AcceptsASingleOpenEndedSlab_AndZeroFreeDays()
    {
        DetentionTariffRules.Validate(0, From, null, [new(1, null, 25m)]).Should().BeEmpty();
        DetentionTariffRules.Validate(3, From, From, [new(4, null, 25m)]).Should().BeEmpty("a one-day validity period is allowed");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(DetentionTariffRules.MaxFreeDays + 1)]
    public void Validate_RejectsFreeDaysOutOfRange(int freeDays)
    {
        Problems(freeDays, null, [new(freeDays + 1, null, 10m)], "FreeDays").Should().ContainSingle().Which.Should().Contain("between 0 and 60");
    }

    [Fact]
    public void Validate_RejectsAnEndDateBeforeTheStart() =>
        Problems(5, From.AddDays(-1), Valid(), "ValidTo").Should().ContainSingle().Which.Should().Contain("cannot be before the start");

    [Fact]
    public void Validate_RequiresAtLeastOneSlab() => Problems(5, null, []).Should().Contain("Add at least one slab.");

    [Fact]
    public void Validate_RequiresTheFirstSlabToStartRightAfterTheFreeDays()
    {
        Problems(5, null, [new(7, 10, 40m), new(11, null, 80m)]).Should().Contain("The first slab must start on day 6, right after the free days.");
        Problems(5, null, [new(5, 10, 40m), new(11, null, 80m)]).Should().Contain(p => p.StartsWith("The first slab must start on day 6"));
    }

    [Fact]
    public void Validate_RequiresSlabsToBeContiguous_NoGapsAndNoOverlaps()
    {
        Problems(5, null, [new(6, 10, 40m), new(12, null, 80m)]).Should().Contain("Slab 2 must start on day 11, right after slab 1 ends.");
        Problems(5, null, [new(6, 10, 40m), new(10, null, 80m)]).Should().Contain("Slab 2 must start on day 11, right after slab 1 ends.");
    }

    [Fact]
    public void Validate_RequiresTheLastSlabToBeOpenEnded_AndOnlyTheLast()
    {
        Problems(5, null, [new(6, 10, 40m), new(11, 20, 80m)]).Should().Contain("The last slab must be open-ended so that long stays are always priced.");
        Problems(5, null, [new(6, null, 40m), new(11, null, 80m)]).Should().Contain("Only the last slab can be open-ended (slab 1 has no end day).");
    }

    [Fact]
    public void Validate_RejectsASlabThatEndsBeforeItStarts() =>
        Problems(5, null, [new(6, 5, 40m), new(6, null, 80m)]).Should().Contain(p => p.Contains("end day cannot be before the start day"));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10_000.01)]
    [InlineData(12.345)]
    public void Validate_RejectsAnInvalidDailyRate(double rate)
    {
        Problems(5, null, [new(6, null, (decimal)rate)]).Should().Contain(p => p.StartsWith("Slab 1: the daily rate must be above 0"));
    }

    [Fact]
    public void Validate_AcceptsTheMaximumRateAndCents() =>
        DetentionTariffRules.Validate(5, From, null, [new(6, null, 10_000m)]).Should().BeEmpty();

    [Fact]
    public void Validate_RejectsTwoSlabsStartingOnTheSameDay() =>
        Problems(5, null, [new(6, 10, 40m), new(6, 10, 50m), new(11, null, 80m)]).Should().Contain("Two slabs start on day 6.");

    [Fact]
    public void Validate_RejectsTooManySlabs()
    {
        var slabs = Enumerable.Range(0, DetentionTariffRules.MaxSlabs + 1)
            .Select(i => new SlabDraft(6 + (i * 5), i == DetentionTariffRules.MaxSlabs ? null : 10 + (i * 5), 10m + i))
            .ToList();

        Problems(5, null, slabs).Should().Contain("A tariff can have at most 10 slabs.");
    }

    [Fact]
    public void Validate_DoesNotDependOnTheOrderTheSlabsAreListedIn() =>
        DetentionTariffRules.Validate(5, From, null, Valid().AsEnumerable().Reverse().ToList()).Should().BeEmpty();
}
