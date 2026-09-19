using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Common;
using UrlShortener.Application.UrlShortening.CreateShortUrl;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Interfaces;
using UrlShortener.Domain.ValueObjects;
using Xunit;

namespace UrlShortener.Application.Tests;

public class CreateShortUrlHandlerTests
{
    private readonly IShortUrlRepository _repository = Substitute.For<IShortUrlRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICodeGenerator _codeGenerator = Substitute.For<ICodeGenerator>();
    private readonly ICustomAliasPolicy _aliasPolicy = Substitute.For<ICustomAliasPolicy>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private CreateShortUrlHandler CreateHandler() =>
        new(_repository, _unitOfWork, _codeGenerator, _aliasPolicy, _clock, NullLogger<CreateShortUrlHandler>.Instance);

    public CreateShortUrlHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Handle_WithGeneratedCode_ReturnsSuccessAndPersists()
    {
        var candidate = ShortCode.Create("abc1234");
        _codeGenerator.GenerateCandidate(Arg.Any<int>()).Returns(candidate);
        _repository.ExistsAsync(candidate, Arg.Any<CancellationToken>()).Returns(false);

        var handler = CreateHandler();
        var result = await handler.Handle(new CreateShortUrlCommand("https://example.com", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Code.Should().Be("abc1234");
        await _repository.Received(1).AddAsync(Arg.Any<ShortUrl>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenGeneratedCodeCollides_RetriesUntilUnique()
    {
        var first = ShortCode.Create("aaaaaaa");
        var second = ShortCode.Create("bbbbbbb");
        _codeGenerator.GenerateCandidate(Arg.Any<int>()).Returns(first, second);
        _repository.ExistsAsync(first, Arg.Any<CancellationToken>()).Returns(true);
        _repository.ExistsAsync(second, Arg.Any<CancellationToken>()).Returns(false);

        var handler = CreateHandler();
        var result = await handler.Handle(new CreateShortUrlCommand("https://example.com", null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Code.Should().Be("bbbbbbb");
    }

    [Fact]
    public async Task Handle_WithTakenCustomAlias_ReturnsConflict()
    {
        _aliasPolicy.IsAllowed("taken", out Arg.Any<string?>()).Returns(x => { x[1] = null; return true; });
        var alias = ShortCode.Create("taken");
        _repository.ExistsAsync(alias, Arg.Any<CancellationToken>()).Returns(true);

        var handler = CreateHandler();
        var result = await handler.Handle(new CreateShortUrlCommand("https://example.com", "taken", null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task Handle_WithRejectedAlias_ReturnsValidationFailed()
    {
        _aliasPolicy.IsAllowed("admin", out Arg.Any<string?>()).Returns(x => { x[1] = "reserved word"; return false; });

        var handler = CreateHandler();
        var result = await handler.Handle(new CreateShortUrlCommand("https://example.com", "admin", null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }
}
