using MediatR;
using UrlShortener.Application.Common;

namespace UrlShortener.Application.UrlShortening.DeactivateShortUrl;

public sealed record DeactivateShortUrlCommand(string Code, string? RequestedByOwnerId) : IRequest<Result<bool>>;
