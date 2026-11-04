using BoxLine.Core.Containers;
using FluentAssertions;

namespace BoxLine.Tests.Core;

/// <remarks>
/// Reference values were worked out by hand and cross-checked with an independent script.
/// Letter values: A=10 B=12 C=13 ... K=21 L=23 ... U=32 V=34 ... Z=38 (11, 22 and 33 are skipped).
/// Weights are 2^0 .. 2^9 for the ten leading characters; check digit = (sum mod 11) mod 10.
///
/// CSQU305438 (the standard published BIC example):
///   C=13*1 + S=30*2 + Q=28*4 + U=32*8 + 3*16 + 0*32 + 5*64 + 4*128 + 3*256 + 8*512
///   = 13 + 60 + 112 + 256 + 48 + 0 + 320 + 512 + 768 + 4096 = 6185;  6185 mod 11 = 3  ->  CSQU3054383
///
/// MSKU123456: M=24 S=30 K=21 U=32 -> 24 + 60 + 84 + 256 + 16 + 64 + 192 + 512 + 1280 + 3072 = 5560;
///   5560 mod 11 = 5  ->  MSKU1234565
///
/// BXLU000003: B=12 X=36 L=23 U=32 -> 12 + 72 + 92 + 256 + 0 + 0 + 0 + 0 + 0 + 1536 = 1968;
///   1968 mod 11 = 10  ->  the check digit becomes 0  ->  BXLU0000030
/// </remarks>
public class Iso6346ValidatorTests
{
    [Theory]
    [InlineData("CSQU3054383")]
    [InlineData("MSKU1234565")]
    [InlineData("BXLU0000030")]
    public void IsValid_AcceptsKnownGoodNumbers(string number)
    {
        Iso6346Validator.IsValid(number).Should().BeTrue();
        Iso6346Validator.Validate(number).Should().BeNull();
    }

    [Theory]
    [InlineData("CSQU3054380")]
    [InlineData("MSKU1234566")]
    public void IsValid_RejectsWrongCheckDigit(string number)
    {
        Iso6346Validator.IsValid(number).Should().BeFalse();
        Iso6346Validator.Validate(number).Should().Contain("Check digit is wrong").And.Contain("should end in");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_RequiresAValue(string? number)
    {
        Iso6346Validator.Validate(number).Should().Be("Container number is required.");
    }

    [Theory]
    [InlineData("CSQU305438")]
    [InlineData("CSQU30543833")]
    [InlineData("CSQ")]
    public void IsValid_RejectsWrongLength(string number)
    {
        Iso6346Validator.IsValid(number).Should().BeFalse();
        Iso6346Validator.Validate(number).Should().Contain("11 characters");
    }

    [Fact]
    public void IsValid_AcceptsLowercaseAndFormattedInput()
    {
        Iso6346Validator.IsValid("csqu3054383").Should().BeTrue();
        Iso6346Validator.IsValid("CSQU 305438-3").Should().BeTrue();
        Iso6346Validator.Normalize(" csqu 305438-3 ").Should().Be("CSQU3054383");
    }

    [Theory]
    [InlineData("CSQA3054383")]
    [InlineData("CSQT3054383")]
    public void IsValid_RejectsInvalidCategoryLetter(string number)
    {
        Iso6346Validator.Validate(number).Should().Contain("category letter U, J or Z");
    }

    [Theory]
    [InlineData("CS1U3054383")]
    [InlineData("1SQU3054383")]
    public void IsValid_RejectsDigitsWhereLettersAreExpected(string number)
    {
        Iso6346Validator.Validate(number).Should().Contain("first four characters must be letters");
    }

    [Theory]
    [InlineData("CSQUA054383")]
    [InlineData("CSQU30543X3")]
    public void IsValid_RejectsLettersWhereDigitsAreExpected(string number)
    {
        Iso6346Validator.Validate(number).Should().Contain("must be digits");
    }

    [Theory]
    [InlineData("CSQU305438", 3)]
    [InlineData("MSKU123456", 5)]
    public void ComputeCheckDigit_MatchesHandCalculation(string firstTen, int expected)
    {
        Iso6346Validator.ComputeCheckDigit(firstTen).Should().Be(expected);
    }

    [Fact]
    public void ComputeCheckDigit_TurnsRemainderTenIntoZero()
    {
        Iso6346Validator.ComputeCheckDigit("BXLU000003").Should().Be(0);
    }

    [Theory]
    [InlineData("CSQU30543")]
    [InlineData("CSQU3054383")]
    [InlineData("CS1U305438")]
    public void ComputeCheckDigit_RejectsMalformedInput(string input)
    {
        var act = () => Iso6346Validator.ComputeCheckDigit(input);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LetterValues_SkipMultiplesOfEleven()
    {
        // K=21 and L=23 straddle 22; U=32 and V=34 straddle 33. Changing only that letter shifts the sum by its weight.
        Iso6346Validator.ComputeCheckDigit("AAKU000000").Should().NotBe(Iso6346Validator.ComputeCheckDigit("AALU000000"));
        // A single letter step of +1 in a position with weight 4 changes the sum by 4*2 (21 -> 23) rather than 4*1.
        // sum(AAKU000000) = 10 + 20 + 84 + 256 = 370 -> 370 mod 11 = 7;  sum(AALU000000) = 10 + 20 + 92 + 256 = 378 -> 378 mod 11 = 4
        Iso6346Validator.ComputeCheckDigit("AAKU000000").Should().Be(7);
        Iso6346Validator.ComputeCheckDigit("AALU000000").Should().Be(4);
    }
}
