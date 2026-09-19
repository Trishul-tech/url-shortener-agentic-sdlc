using MediatR;
using UrlShortener.Application.Common;

namespace UrlShortener.Application.UrlShortening.CreateShortUrl;

public sealed record CreateShortUrlCommand(
    string TargetUrl,
    string? CustomAlias,
    string? OwnerId,
    DateTimeOffset? ExpiresAtUtc) : IRequest<Result<CreateShortUrlResult>>;

public sealed record CreateShortUrlResult(
    string Code,
    string ShortUrl,
    string TargetUrl,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc);
