namespace UrlShortener.Domain.Entities;

/// <summary>
/// An append-only analytics record for a single redirect. Kept as a
/// separate table (not embedded in ShortUrl) so high-volume writes never
/// contend with the aggregate's optimistic-concurrency token.
/// </summary>
public sealed class ClickEvent
{
    public long Id { get; private set; }
    public Guid ShortUrlId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? RefererHost { get; private set; }
    public string? UserAgent { get; private set; }
    public string? CountryCode { get; private set; }
    /// <summary>SHA-256 hash of the caller IP; the raw IP is never persisted (PII minimization).</summary>
    public string? IpHash { get; private set; }

    private ClickEvent() { } // EF Core

    public static ClickEvent Create(
        Guid shortUrlId,
        DateTimeOffset occurredAtUtc,
        string? refererHost,
        string? userAgent,
        string? countryCode,
        string? ipHash)
    {
        return new ClickEvent
        {
            ShortUrlId = shortUrlId,
            OccurredAtUtc = occurredAtUtc,
            RefererHost = Truncate(refererHost, 255),
            UserAgent = Truncate(userAgent, 512),
            CountryCode = countryCode,
            IpHash = ipHash
        };
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
}
