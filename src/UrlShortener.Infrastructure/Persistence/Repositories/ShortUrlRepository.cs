using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Interfaces;
using UrlShortener.Domain.ValueObjects;

namespace UrlShortener.Infrastructure.Persistence.Repositories;

public sealed class ShortUrlRepository : IShortUrlRepository
{
    private readonly AppDbContext _db;

    public ShortUrlRepository(AppDbContext db) => _db = db;

    public async Task<ShortUrl?> GetByCodeAsync(ShortCode code, CancellationToken ct) =>
        await _db.ShortUrls.SingleOrDefaultAsync(x => x.Code == code, ct);

    public async Task<bool> ExistsAsync(ShortCode code, CancellationToken ct) =>
        await _db.ShortUrls.AnyAsync(x => x.Code == code, ct);

    public async Task AddAsync(ShortUrl shortUrl, CancellationToken ct) =>
        await _db.ShortUrls.AddAsync(shortUrl, ct);

    public void Update(ShortUrl shortUrl) => _db.ShortUrls.Update(shortUrl);

    public async Task<IReadOnlyList<ShortUrl>> GetByOwnerAsync(string ownerId, int skip, int take, CancellationToken ct) =>
        await _db.ShortUrls
            .Where(x => x.OwnerId == ownerId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
}
