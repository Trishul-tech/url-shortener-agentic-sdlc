using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Interfaces;

namespace UrlShortener.Infrastructure.Persistence.Repositories;

public sealed class ClickEventRepository : IClickEventRepository
{
    private readonly AppDbContext _db;

    public ClickEventRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ClickEvent clickEvent, CancellationToken ct) =>
        await _db.ClickEvents.AddAsync(clickEvent, ct);

    public async Task<IReadOnlyList<ClickEvent>> GetForShortUrlAsync(
        Guid shortUrlId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct) =>
        await _db.ClickEvents
            .Where(x => x.ShortUrlId == shortUrlId && x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ToListAsync(ct);

    public async Task<long> CountForShortUrlAsync(
        Guid shortUrlId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct) =>
        await _db.ClickEvents
            .LongCountAsync(x => x.ShortUrlId == shortUrlId && x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc, ct);

    public async Task<IReadOnlyDictionary<DateOnly, long>> GetDailyCountsAsync(
        Guid shortUrlId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var rows = await _db.ClickEvents
            .Where(x => x.ShortUrlId == shortUrlId && x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc)
            .Select(x => x.OccurredAtUtc)
            .ToListAsync(ct);

        return rows
            .GroupBy(t => DateOnly.FromDateTime(t.UtcDateTime))
            .ToDictionary(g => g.Key, g => (long)g.Count());
    }
}
