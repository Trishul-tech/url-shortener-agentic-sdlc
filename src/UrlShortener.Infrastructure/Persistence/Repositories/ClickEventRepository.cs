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

    // NOTE: SQLite's EF Core provider can only translate equality comparisons on
    // DateTimeOffset columns into SQL - a >= / <= range comparison throws
    // InvalidOperationException ("could not be translated") at runtime. So each
    // method below filters by ShortUrlId in SQL (an indexed, translatable
    // equality check) and applies the date-range filter in memory afterward.
    // Fine for this prototype's click volumes; a production system on SQL
    // Server/Postgres wouldn't need this workaround.

    public async Task<IReadOnlyList<ClickEvent>> GetForShortUrlAsync(
        Guid shortUrlId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var events = await _db.ClickEvents
            .Where(x => x.ShortUrlId == shortUrlId)
            .ToListAsync(ct);

        return events
            .Where(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ToList();
    }

    public async Task<long> CountForShortUrlAsync(
        Guid shortUrlId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var occurredAt = await _db.ClickEvents
            .Where(x => x.ShortUrlId == shortUrlId)
            .Select(x => x.OccurredAtUtc)
            .ToListAsync(ct);

        return occurredAt.LongCount(t => t >= fromUtc && t <= toUtc);
    }

    public async Task<IReadOnlyDictionary<DateOnly, long>> GetDailyCountsAsync(
        Guid shortUrlId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var rows = await _db.ClickEvents
            .Where(x => x.ShortUrlId == shortUrlId)
            .Select(x => x.OccurredAtUtc)
            .ToListAsync(ct);

        return rows
            .Where(t => t >= fromUtc && t <= toUtc)
            .GroupBy(t => DateOnly.FromDateTime(t.UtcDateTime))
            .ToDictionary(g => g.Key, g => (long)g.Count());
    }
}
