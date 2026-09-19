using MediatR;
using UrlShortener.Application.Common;

namespace UrlShortener.Application.Analytics.GetUrlAnalytics;

public sealed record GetUrlAnalyticsQuery(
    string Code,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc) : IRequest<Result<UrlAnalyticsResult>>;

public sealed record UrlAnalyticsResult(
    string Code,
    long TotalClicks,
    long ClicksInRange,
    IReadOnlyDictionary<DateOnly, long> DailyClicks,
    DateTimeOffset? LastAccessedAtUtc);
