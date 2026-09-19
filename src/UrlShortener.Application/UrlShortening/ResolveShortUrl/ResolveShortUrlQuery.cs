using MediatR;
using UrlShortener.Application.Common;

namespace UrlShortener.Application.UrlShortening.ResolveShortUrl;

public sealed record ResolveShortUrlQuery(
    string Code,
    string? RefererHost,
    string? UserAgent,
    string? ClientIp) : IRequest<Result<string>>;
