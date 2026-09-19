namespace UrlShortener.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Skip, int Take);
