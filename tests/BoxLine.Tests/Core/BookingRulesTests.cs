using BoxLine.Core.Bookings;
using FluentAssertions;

namespace BoxLine.Tests.Core;

public class BookingRulesTests
{
    private static BookingDraft Valid() => new(1, 2, "Cotton garments", [new BookingLineDraft(1, 2), new BookingLineDraft(2, 1)]);

    [Fact]
    public void Validate_AcceptsAWellFormedDraft() => BookingRules.Validate(Valid()).Should().BeEmpty();

    [Fact]
    public void Validate_RejectsSamePortOfLoadingAndDischarge()
    {
        var errors = BookingRules.Validate(Valid() with { PodId = 1 });

        errors["PodId"].Should().ContainSingle().Which.Should().Contain("must differ");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(BookingRules.MaxQuantityPerLine + 1)]
    public void Validate_RejectsQuantityOutsideTheAllowedRange(int quantity)
    {
        var errors = BookingRules.Validate(Valid() with { Lines = [new BookingLineDraft(1, quantity)] });

        errors["Lines"].Should().ContainSingle().Which.Should().Contain("between 1 and 50");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(BookingRules.MaxQuantityPerLine)]
    public void Validate_AcceptsQuantityAtTheBoundaries(int quantity)
    {
        BookingRules.Validate(Valid() with { Lines = [new BookingLineDraft(1, quantity)] }).Should().BeEmpty();
    }

    [Fact]
    public void Validate_RequiresAtLeastOneLine()
    {
        BookingRules.Validate(Valid() with { Lines = [] })["Lines"].Should().ContainSingle().Which.Should().Contain("at least one");
    }

    [Fact]
    public void Validate_RejectsTheSameContainerTypeTwice()
    {
        var errors = BookingRules.Validate(Valid() with { Lines = [new BookingLineDraft(1, 1), new BookingLineDraft(1, 2)] });

        errors["Lines"].Should().ContainSingle().Which.Should().Contain("only once");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Validate_RequiresCargoDescription(string? cargo)
    {
        BookingRules.Validate(Valid() with { CargoDescription = cargo }).Should().ContainKey("CargoDescription");
    }

    [Fact]
    public void Validate_RejectsAnOverlongCargoDescription()
    {
        var errors = BookingRules.Validate(Valid() with { CargoDescription = new string('x', BookingRules.MaxCargoDescriptionLength + 1) });

        errors.Should().ContainKey("CargoDescription");
    }

    [Fact]
    public void Validate_ReportsEveryProblemAtOnce()
    {
        var errors = BookingRules.Validate(new BookingDraft(1, 1, "", []));

        errors.Keys.Should().BeEquivalentTo("PodId", "CargoDescription", "Lines");
    }
}
