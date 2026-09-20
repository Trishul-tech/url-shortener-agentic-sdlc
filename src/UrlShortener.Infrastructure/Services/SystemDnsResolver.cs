using System.Net;
using UrlShortener.Application.Abstractions;

namespace UrlShortener.Infrastructure.Services;

/// <summary>Real DNS resolution via System.Net.Dns.</summary>
public sealed class SystemDnsResolver : IDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) =>
        Dns.GetHostAddressesAsync(host, ct);
}
