using BoxLine.Core.Bookings;
using FluentAssertions;

namespace BoxLine.Tests.Core;

public class BookingNumberTests
{
    private static readonly DateTime November2026 = new(2026, 11, 4, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Format_UsesMonthAndAFourDigitSequence() =>
        BookingNumber.Format(November2026, 7).Should().Be("BL-202611-0007");

    [Fact]
    public void Prefix_ChangesWithTheMonth() =>
        BookingNumber.Prefix(new DateTime(2027, 1, 31, 23, 59, 0, DateTimeKind.Utc)).Should().Be("BL-202701-");

    [Theory]
    [InlineData(null, 1)]
    [InlineData("BL-202611-0001", 2)]
    [InlineData("BL-202611-0099", 100)]
    [InlineData("BL-202611-9999", 10000)]
    [InlineData("BL-202610-0042", 1)]
    [InlineData("garbage", 1)]
    public void NextSequence_ContinuesWithinTheMonthAndRestartsOtherwise(string? last, int expected) =>
        BookingNumber.NextSequence(last, "BL-202611-").Should().Be(expected);
}
