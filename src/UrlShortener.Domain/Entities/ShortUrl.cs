using UrlShortener.Domain.Exceptions;
using UrlShortener.Domain.ValueObjects;

namespace UrlShortener.Domain.Entities;

/// <summary>
/// Aggregate root for a shortened URL. All state transitions (deactivate,
/// record a click, extend expiry) go through methods here so invariants
/// can never be violated from outside the aggregate.
/// </summary>
public sealed class ShortUrl
{
    public Guid Id { get; private set; }
    public ShortCode Code { get; private set; } = null!;
    public string TargetUrl { get; private set; } = null!;
    public string? OwnerId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public long ClickCount { get; private set; }
    public DateTimeOffset? LastAccessedAtUtc { get; private set; }

    /// <summary>Concurrency token: EF Core uses this to detect lost-update races on click increments.</summary>
    public uint Version { get; private set; }

    private ShortUrl() { } // EF Core

    public static ShortUrl Create(
        ShortCode code,
        string targetUrl,
        DateTimeOffset nowUtc,
        string? ownerId = null,
        DateTimeOffset? expiresAtUtc = null)
    {
        ValidateTargetUrl(targetUrl);

        if (expiresAtUtc is { } exp && exp <= nowUtc)
            throw new InvalidTargetUrlException(targetUrl, "expiry must be in the future");

        return new ShortUrl
        {
            Id = Guid.NewGuid(),
            Code = code,
            TargetUrl = targetUrl,
            OwnerId = ownerId,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
            IsActive = true,
            ClickCount = 0,
            Version = 0
        };
    }

    private static void ValidateTargetUrl(string targetUrl)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
            throw new InvalidTargetUrlException(targetUrl ?? string.Empty, "must not be empty");

        if (targetUrl.Length > 2048)
            throw new InvalidTargetUrlException(targetUrl, "must not exceed 2048 characters");

        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidTargetUrlException(targetUrl, "must be an absolute http(s) URL");
        }
    }

    /// <summary>
    /// Resolves the aggregate for redirect purposes, enforcing active/expiry
    /// invariants at the point of use rather than trusting a cached flag.
    /// </summary>
    public string Resolve(DateTimeOffset nowUtc)
    {
        if (!IsActive)
            throw new ShortUrlDeactivatedException(Code.Value);

        if (ExpiresAtUtc is { } exp && exp <= nowUtc)
            throw new ShortUrlExpiredException(Code.Value);

        return TargetUrl;
    }

    public void RecordClick(DateTimeOffset nowUtc)
    {
        ClickCount++;
        LastAccessedAtUtc = nowUtc;
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (ExpiresAtUtc is { } exp && exp <= nowUtc)
            throw new ShortUrlExpiredException(Code.Value);

        IsActive = true;
    }

    public bool IsExpired(DateTimeOffset nowUtc) => ExpiresAtUtc is { } exp && exp <= nowUtc;
}
