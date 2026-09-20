using System.Net;

namespace UrlShortener.Application.Abstractions;

/// <summary>
/// Resolves a hostname to its IP addresses. Abstracted behind an interface
/// (rather than calling System.Net.Dns directly) so SSRF validation
/// (see Security/SsrfGuard.cs) does not force integration tests to depend
/// on live DNS/network access - CustomWebApplicationFactory swaps in a
/// fake resolver for a fast, hermetic, deterministic test suite.
/// </summary>
public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
}
