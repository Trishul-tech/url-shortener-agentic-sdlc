namespace UrlShortener.Api.Contracts;

public sealed record CreateShortUrlRequest(string TargetUrl, string? CustomAlias, string? OwnerId, DateTimeOffset? ExpiresAtUtc);

public sealed record CreateShortUrlResponse(string Code, string ShortUrl, string TargetUrl, DateTimeOffset CreatedAtUtc, DateTimeOffset? ExpiresAtUtc);

public sealed record AnalyticsResponse(string Code, long TotalClicks, long ClicksInRange, DateTimeOffset? LastAccessedAtUtc, IReadOnlyDictionary<string, long> DailyClicks);

public sealed record ProblemResponse(string Title, string ErrorCode, string Detail);
