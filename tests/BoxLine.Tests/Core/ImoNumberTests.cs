using BoxLine.Core.Vessels;
using FluentAssertions;

namespace BoxLine.Tests.Core;

/// <remarks>
/// Reference values worked out by hand and cross-checked with an independent script. Weights for the first six
/// digits are 7, 6, 5, 4, 3, 2 and the check digit is the last digit of the sum.
///   907472 : 9*7 + 0*6 + 7*5 + 4*4 + 7*3 + 2*2 = 63 + 0 + 35 + 16 + 21 + 4 = 139 -> 9   (9074729)
///   932148 : 9*7 + 3*6 + 2*5 + 1*4 + 4*3 + 8*2 = 63 + 18 + 10 + 4 + 12 + 16 = 123 -> 3  (9321483)
///   900012 : 9*7 + 0 + 0 + 0 + 1*3 + 2*2 = 63 + 3 + 4 = 70 -> 0                         (9000120)
/// </remarks>
public class ImoNumberTests
{
    [Theory]
    [InlineData("9074729")]
    [InlineData("9321483")]
    [InlineData("9000120")]
    public void IsValid_AcceptsKnownGoodNumbers(string imo)
    {
        ImoNumber.IsValid(imo).Should().BeTrue();
        ImoNumber.Validate(imo).Should().BeNull();
    }

    [Theory]
    [InlineData("9074728", 9, 8)]
    [InlineData("9321480", 3, 0)]
    [InlineData("9000121", 0, 1)]
    public void Validate_RejectsAWrongCheckDigit_AndSaysWhatItShouldBe(string imo, int expected, int actual)
    {
        ImoNumber.Validate(imo).Should().Be($"Check digit is wrong: {imo[..6]} should end in {expected}, not {actual}.");
    }

    [Theory]
    [InlineData("907472")]
    [InlineData("90747290")]
    [InlineData("90747A9")]
    [InlineData("9O74729")]
    public void Validate_RejectsWrongLengthAndNonDigits(string imo)
    {
        ImoNumber.Validate(imo).Should().Be("IMO number must be exactly 7 digits.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Validate_RequiresAValue(string? imo) => ImoNumber.Validate(imo).Should().Be("IMO number is required.");

    [Theory]
    [InlineData("IMO 9074729")]
    [InlineData("imo9074729")]
    [InlineData("IMO-9074-729")]
    [InlineData(" 9074 729 ")]
    public void Normalize_StripsPrefixSpacesAndHyphens(string input)
    {
        ImoNumber.Normalize(input).Should().Be("9074729");
        ImoNumber.IsValid(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("907472", 9)]
    [InlineData("932148", 3)]
    [InlineData("900012", 0)]
    public void ComputeCheckDigit_MatchesHandCalculation(string firstSix, int expected) =>
        ImoNumber.ComputeCheckDigit(firstSix).Should().Be(expected);

    [Theory]
    [InlineData("90747")]
    [InlineData("9074729")]
    [InlineData("90747X")]
    public void ComputeCheckDigit_RejectsMalformedInput(string input)
    {
        var act = () => ImoNumber.ComputeCheckDigit(input);

        act.Should().Throw<ArgumentException>();
    }
}
