namespace UrlShortener.Application.Abstractions;

/// <summary>
/// Thin cache abstraction over the redirect hot-path lookup, so the
/// implementation can move from in-memory to a distributed cache (Redis)
/// without touching application logic.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct) where T : class;
    Task RemoveAsync(string key, CancellationToken ct);
}
