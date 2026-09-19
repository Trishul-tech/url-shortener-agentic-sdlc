using UrlShortener.Domain.Entities;

namespace UrlShortener.Domain.Interfaces;

public interface IClickEventRepository
{
    Task AddAsync(ClickEvent clickEvent, CancellationToken ct);

    Task<IReadOnlyList<ClickEvent>> GetForShortUrlAsync(
        Guid shortUrlId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);

    Task<long> CountForShortUrlAsync(
        Guid shortUrlId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);

    Task<IReadOnlyDictionary<DateOnly, long>> GetDailyCountsAsync(
        Guid shortUrlId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
}
