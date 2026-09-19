using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.Extensions.Logging;
using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Common;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Exceptions;
using UrlShortener.Domain.Interfaces;
using UrlShortener.Domain.ValueObjects;

namespace UrlShortener.Application.UrlShortening.ResolveShortUrl;

/// <summary>
/// Resolves a short code to its target URL for redirect. This is the
/// hottest path in the system, so it reads through a cache first and
/// only touches the write-side repository to persist analytics -
/// click counting happens on the entity but is saved best-effort so a
/// slow analytics write never blocks the redirect response.
/// </summary>
public sealed class ResolveShortUrlHandler : IRequestHandler<ResolveShortUrlQuery, Result<string>>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly IShortUrlRepository _repository;
    private readonly IClickEventRepository _clickEventRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly IClock _clock;
    private readonly ILogger<ResolveShortUrlHandler> _logger;

    public ResolveShortUrlHandler(
        IShortUrlRepository repository,
        IClickEventRepository clickEventRepository,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        IClock clock,
        ILogger<ResolveShortUrlHandler> logger)
    {
        _repository = repository;
        _clickEventRepository = clickEventRepository;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<string>> Handle(ResolveShortUrlQuery request, CancellationToken ct)
    {
        ShortCode code;
        try
        {
            code = ShortCode.Create(request.Code);
        }
        catch (DomainException)
        {
            return Result<string>.Failure("Invalid short code format.", ErrorCodes.NotFound);
        }

        var cacheKey = $"resolve:{code.Value}";
        var cached = await _cache.GetAsync<CachedTarget>(cacheKey, ct);

        ShortUrl? shortUrl = null;
        string targetUrl;

        if (cached is not null)
        {
            targetUrl = cached.TargetUrl;
            // Still fetch the entity to record the click; cache only shortcuts the redirect decision.
            shortUrl = await _repository.GetByCodeAsync(code, ct);
        }
        else
        {
            shortUrl = await _repository.GetByCodeAsync(code, ct);
            if (shortUrl is null)
                return Result<string>.Failure($"Short URL '{code.Value}' not found.", ErrorCodes.NotFound);

            try
            {
                targetUrl = shortUrl.Resolve(_clock.UtcNow);
            }
            catch (ShortUrlExpiredException)
            {
                return Result<string>.Failure($"Short URL '{code.Value}' has expired.", ErrorCodes.Expired);
            }
            catch (ShortUrlDeactivatedException)
            {
                return Result<string>.Failure($"Short URL '{code.Value}' has been deactivated.", ErrorCodes.Deactivated);
            }

            await _cache.SetAsync(cacheKey, new CachedTarget(targetUrl), CacheTtl, ct);
        }

        if (shortUrl is not null)
        {
            try
            {
                shortUrl.RecordClick(_clock.UtcNow);
                _repository.Update(shortUrl);

                await _clickEventRepository.AddAsync(
                    ClickEvent.Create(
                        shortUrl.Id,
                        _clock.UtcNow,
                        request.RefererHost,
                        request.UserAgent,
                        countryCode: null,
                        ipHash: HashIp(request.ClientIp)),
                    ct);

                await _unitOfWork.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Analytics is best-effort: never fail a redirect because click tracking failed.
                _logger.LogWarning(ex, "Failed to record click analytics for {Code}", code.Value);
            }
        }

        return Result<string>.Success(targetUrl);
    }

    private static string? HashIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
        return Convert.ToHexString(bytes);
    }

    private sealed record CachedTarget(string TargetUrl);
}
