using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Common;
using UrlShortener.Application.UrlShortening.ResolveShortUrl;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Interfaces;
using UrlShortener.Domain.ValueObjects;
using Xunit;

namespace UrlShortener.Application.Tests;

public class ResolveShortUrlHandlerTests
{
    private readonly IShortUrlRepository _repository = Substitute.For<IShortUrlRepository>();
    private readonly IClickEventRepository _clickEventRepository = Substitute.For<IClickEventRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private ResolveShortUrlHandler CreateHandler() =>
        new(_repository, _clickEventRepository, _unitOfWork, _cache, _clock, NullLogger<ResolveShortUrlHandler>.Instance);

    public ResolveShortUrlHandlerTests() => _clock.UtcNow.Returns(Now);

    [Fact]
    public async Task Handle_WithUnknownCode_ReturnsNotFound()
    {
        _repository.GetByCodeAsync(Arg.Any<ShortCode>(), Arg.Any<CancellationToken>()).Returns((ShortUrl?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new ResolveShortUrlQuery("abc1234", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WithActiveCode_ReturnsTargetAndRecordsClick()
    {
        var shortUrl = ShortUrl.Create(ShortCode.Create("abc1234"), "https://example.com", Now);
        _repository.GetByCodeAsync(Arg.Any<ShortCode>(), Arg.Any<CancellationToken>()).Returns(shortUrl);

        var handler = CreateHandler();
        var result = await handler.Handle(new ResolveShortUrlQuery("abc1234", "google.com", "test-agent", "1.2.3.4"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("https://example.com");
        shortUrl.ClickCount.Should().Be(1);
        await _clickEventRepository.Received(1).AddAsync(Arg.Any<ClickEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithDeactivatedCode_ReturnsDeactivatedError()
    {
        var shortUrl = ShortUrl.Create(ShortCode.Create("abc1234"), "https://example.com", Now);
        shortUrl.Deactivate();
        _repository.GetByCodeAsync(Arg.Any<ShortCode>(), Arg.Any<CancellationToken>()).Returns(shortUrl);

        var handler = CreateHandler();
        var result = await handler.Handle(new ResolveShortUrlQuery("abc1234", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Deactivated);
    }

    [Fact]
    public async Task Handle_WithExpiredCode_ReturnsExpiredError()
    {
        var shortUrl = ShortUrl.Create(ShortCode.Create("abc1234"), "https://example.com", Now, expiresAtUtc: Now.AddDays(1));
        _repository.GetByCodeAsync(Arg.Any<ShortCode>(), Arg.Any<CancellationToken>()).Returns(shortUrl);
        _clock.UtcNow.Returns(Now.AddDays(2));

        var handler = CreateHandler();
        var result = await handler.Handle(new ResolveShortUrlQuery("abc1234", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Expired);
    }

    [Fact]
    public async Task Handle_WhenAnalyticsWriteThrows_StillReturnsSuccess()
    {
        var shortUrl = ShortUrl.Create(ShortCode.Create("abc1234"), "https://example.com", Now);
        _repository.GetByCodeAsync(Arg.Any<ShortCode>(), Arg.Any<CancellationToken>()).Returns(shortUrl);
        _clickEventRepository.AddAsync(Arg.Any<ClickEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("db unavailable"));

        var handler = CreateHandler();
        var result = await handler.Handle(new ResolveShortUrlQuery("abc1234", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("a failure to record analytics must never fail the redirect itself");
    }
}
