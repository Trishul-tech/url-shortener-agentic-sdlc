using UrlShortener.Domain.Entities;
using UrlShortener.Domain.ValueObjects;

namespace UrlShortener.Domain.Interfaces;

public interface IShortUrlRepository
{
    Task<ShortUrl?> GetByCodeAsync(ShortCode code, CancellationToken ct);
    Task<bool> ExistsAsync(ShortCode code, CancellationToken ct);
    Task AddAsync(ShortUrl shortUrl, CancellationToken ct);
    /// <summary>Marks the tracked aggregate for update; persistence happens on SaveChangesAsync via IUnitOfWork.</summary>
    void Update(ShortUrl shortUrl);
    Task<IReadOnlyList<ShortUrl>> GetByOwnerAsync(string ownerId, int skip, int take, CancellationToken ct);
}
