using MediatR;
using Microsoft.Extensions.Logging;
using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Common;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Interfaces;
using UrlShortener.Domain.ValueObjects;

namespace UrlShortener.Application.UrlShortening.CreateShortUrl;

/// <summary>
/// Creates a short URL. Custom aliases are taken as-is (subject to a
/// uniqueness check); generated codes retry on collision with bounded
/// attempts, since a Base62 random code can (rarely) already exist.
/// </summary>
public sealed class CreateShortUrlHandler : IRequestHandler<CreateShortUrlCommand, Result<CreateShortUrlResult>>
{
    private const int MaxGenerationAttempts = 5;

    private readonly IShortUrlRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICodeGenerator _codeGenerator;
    private readonly ICustomAliasPolicy _aliasPolicy;
    private readonly IClock _clock;
    private readonly ILogger<CreateShortUrlHandler> _logger;

    public CreateShortUrlHandler(
        IShortUrlRepository repository,
        IUnitOfWork unitOfWork,
        ICodeGenerator codeGenerator,
        ICustomAliasPolicy aliasPolicy,
        IClock clock,
        ILogger<CreateShortUrlHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _codeGenerator = codeGenerator;
        _aliasPolicy = aliasPolicy;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<CreateShortUrlResult>> Handle(CreateShortUrlCommand request, CancellationToken ct)
    {
        ShortCode code;

        if (!string.IsNullOrWhiteSpace(request.CustomAlias))
        {
            if (!_aliasPolicy.IsAllowed(request.CustomAlias, out var reason))
                return Result<CreateShortUrlResult>.Failure($"Alias rejected: {reason}", ErrorCodes.ValidationFailed);

            code = ShortCode.Create(request.CustomAlias);
            if (await _repository.ExistsAsync(code, ct))
                return Result<CreateShortUrlResult>.Failure($"Alias '{request.CustomAlias}' is already taken.", ErrorCodes.Conflict);
        }
        else
        {
            code = await GenerateUniqueCodeAsync(ct);
        }

        var shortUrl = ShortUrl.Create(code, request.TargetUrl, _clock.UtcNow, request.OwnerId, request.ExpiresAtUtc);

        await _repository.AddAsync(shortUrl, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Short URL created: {Code} -> {TargetHost}", code.Value, SafeHost(request.TargetUrl));

        return Result<CreateShortUrlResult>.Success(new CreateShortUrlResult(
            code.Value, $"https://short.link/{code.Value}", request.TargetUrl, shortUrl.CreatedAtUtc, shortUrl.ExpiresAtUtc));
    }

    private async Task<ShortCode> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            var candidate = _codeGenerator.GenerateCandidate();
            if (!await _repository.ExistsAsync(candidate, ct))
                return candidate;

            _logger.LogWarning("Short code collision on attempt {Attempt} for candidate {Candidate}", attempt + 1, candidate.Value);
        }

        // Widen the search space rather than fail the request outright.
        var fallback = _codeGenerator.GenerateCandidate(length: 9);
        if (await _repository.ExistsAsync(fallback, ct))
            throw new InvalidOperationException("Unable to allocate a unique short code after exhausting retries.");

        return fallback;
    }

    private static string SafeHost(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "unknown";
}
