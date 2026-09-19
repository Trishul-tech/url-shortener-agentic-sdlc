using Microsoft.Extensions.Caching.Memory;
using UrlShortener.Application.Abstractions;

namespace UrlShortener.Infrastructure.Services;

/// <summary>
/// In-process cache for the prototype. Swappable for a distributed
/// (Redis) implementation behind ICacheService once the service runs
/// on more than one instance - see docs/architecture.md.
/// </summary>
public sealed class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;

    public MemoryCacheService(IMemoryCache cache) => _cache = cache;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class =>
        Task.FromResult(_cache.TryGetValue(key, out T? value) ? value : null);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct) where T : class
    {
        _cache.Set(key, value, ttl);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }
}
