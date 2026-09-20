using FluentAssertions;
using UrlShortener.Domain.Exceptions;
using UrlShortener.Domain.ValueObjects;
using Xunit;

namespace UrlShortener.Domain.Tests;

public class ShortCodeTests
{
    [Theory]
    [InlineData("abc123")]
    [InlineData("AZaz09")]
    [InlineData("A1b2C3d4")]
    [InlineData("with-dash")]     // custom aliases may contain hyphens
    [InlineData("with_underscore")] // custom aliases may contain underscores
    public void Create_WithValidCandidate_Succeeds(string candidate)
    {
        var code = ShortCode.Create(candidate);
        code.Value.Should().Be(candidate);
    }

    [Theory]
    [InlineData("ab")]           // too short (below MinLength of 3)
    [InlineData("has space")]     // invalid characters
    [InlineData("has#hash")]      // invalid characters
    [InlineData("")]
    public void Create_WithInvalidCandidate_Throws(string candidate)
    {
        var act = () => ShortCode.Create(candidate);
        act.Should().Throw<InvalidTargetUrlException>();
    }

    [Fact]
    public void TwoShortCodesWithSameValue_AreEqualByValue()
    {
        var a = ShortCode.Create("abc1234");
        var b = ShortCode.Create("abc1234");

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
