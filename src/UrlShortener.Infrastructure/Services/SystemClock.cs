using UrlShortener.Domain.Interfaces;

namespace UrlShortener.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
