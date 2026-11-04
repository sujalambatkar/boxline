using BoxLine.Core.Ports;
using FluentAssertions;

namespace BoxLine.Tests.Core;

public class UnLocodeTests
{
    [Theory]
    [InlineData("INNSA")]
    [InlineData("AEJEA")]
    [InlineData("nlrtm")]
    [InlineData("GB4AB")]
    public void IsValid_AcceptsWellFormedCodes(string code) => UnLocode.IsValid(code).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("INNS")]
    [InlineData("INNSAX")]
    [InlineData("1NNSA")]
    [InlineData("IN1SA")]
    [InlineData("IN-SA")]
    public void IsValid_RejectsMalformedCodes(string code) => UnLocode.IsValid(code).Should().BeFalse();

    [Fact]
    public void Normalize_TrimsAndUpperCases() => UnLocode.Normalize(" innsa ").Should().Be("INNSA");
}
