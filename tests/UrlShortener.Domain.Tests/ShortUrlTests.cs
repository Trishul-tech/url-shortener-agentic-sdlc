using FluentAssertions;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Exceptions;
using UrlShortener.Domain.ValueObjects;
using Xunit;

namespace UrlShortener.Domain.Tests;

public class ShortUrlTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidData_SetsExpectedState()
    {
        var code = ShortCode.Create("abc1234");
        var shortUrl = ShortUrl.Create(code, "https://example.com/page", Now);

        shortUrl.Code.Should().Be(code);
        shortUrl.TargetUrl.Should().Be("https://example.com/page");
        shortUrl.IsActive.Should().BeTrue();
        shortUrl.ClickCount.Should().Be(0);
        shortUrl.CreatedAtUtc.Should().Be(Now);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/file")]
    [InlineData("")]
    public void Create_WithInvalidTargetUrl_Throws(string target)
    {
        var code = ShortCode.Create("abc1234");
        var act = () => ShortUrl.Create(code, target, Now);
        act.Should().Throw<InvalidTargetUrlException>();
    }

    [Fact]
    public void Create_WithPastExpiry_Throws()
    {
        var code = ShortCode.Create("abc1234");
        var act = () => ShortUrl.Create(code, "https://example.com", Now, expiresAtUtc: Now.AddMinutes(-1));
        act.Should().Throw<InvalidTargetUrlException>();
    }

    [Fact]
    public void Resolve_WhenActiveAndNotExpired_ReturnsTargetUrl()
    {
        var shortUrl = ShortUrl.Create(ShortCode.Create("abc1234"), "https://example.com", Now);
        shortUrl.Resolve(Now.AddMinutes(1)).Should().Be("https://example.com");
    }

    [Fact]
    public void Resolve_WhenDeactivated_ThrowsDeactivatedException()
    {
        var shortUrl = ShortUrl.Create(ShortCode.Create("abc1234"), "https://example.com", Now);
        shortUrl.Deactivate();

        var act = () => shortUrl.Resolve(Now);
        act.Should().Throw<ShortUrlDeactivatedException>();
    }

    [Fact]
    public void Resolve_WhenExpired_ThrowsExpiredException()
    {
        var shortUrl = ShortUrl.Create(ShortCode.Create("abc1234"), "https://example.com", Now, expiresAtUtc: Now.AddDays(1));
        var act = () => shortUrl.Resolve(Now.AddDays(2));
        act.Should().Throw<ShortUrlExpiredException>();
    }

    [Fact]
    public void RecordClick_IncrementsCountAndSetsLastAccessed()
    {
        var shortUrl = ShortUrl.Create(ShortCode.Create("abc1234"), "https://example.com", Now);

        shortUrl.RecordClick(Now.AddMinutes(5));
        shortUrl.RecordClick(Now.AddMinutes(10));

        shortUrl.ClickCount.Should().Be(2);
        shortUrl.LastAccessedAtUtc.Should().Be(Now.AddMinutes(10));
    }

    [Fact]
    public void Reactivate_WhenExpired_Throws()
    {
        var shortUrl = ShortUrl.Create(ShortCode.Create("abc1234"), "https://example.com", Now, expiresAtUtc: Now.AddDays(1));
        shortUrl.Deactivate();

        var act = () => shortUrl.Reactivate(Now.AddDays(2));
        act.Should().Throw<ShortUrlExpiredException>();
    }

    [Fact]
    public void Reactivate_WhenNotExpired_SetsActive()
    {
        var shortUrl = ShortUrl.Create(ShortCode.Create("abc1234"), "https://example.com", Now);
        shortUrl.Deactivate();
        shortUrl.Reactivate(Now.AddMinutes(1));
        shortUrl.IsActive.Should().BeTrue();
    }
}
