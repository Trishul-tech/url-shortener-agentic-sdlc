using System.Net;
using System.Net.Sockets;
using UrlShortener.Application.Abstractions;

namespace UrlShortener.Application.Security;

/// <summary>
/// Blocks short-URL targets that would let the redirect endpoint be used
/// as an SSRF (Server-Side Request Forgery) vector - i.e. targets that
/// resolve to a private, loopback, link-local, or cloud-metadata address
/// instead of a genuine public destination. This is a creation-time
/// check: it inspects the DNS answer when the short URL is created, which
/// is a real, documented limitation (a DNS TOCTOU gap - the resolved
/// address could change before the redirect actually fires - see
/// docs/testing-limitations-tradeoffs.md) rather than a guarantee the
/// target can never be internal at redirect time.
/// </summary>
public sealed class SsrfGuard
{
    // 169.254.169.254 is the cloud-metadata endpoint on AWS/Azure/GCP. It
    // also falls inside the 169.254.0.0/16 link-local range blocked below,
    // but it is called out by name so the intent is unmistakable.
    private static readonly IPAddress CloudMetadataAddress = IPAddress.Parse("169.254.169.254");

    private readonly IDnsResolver _dnsResolver;

    public SsrfGuard(IDnsResolver dnsResolver) => _dnsResolver = dnsResolver;

    public async Task<bool> IsSafePublicTargetAsync(string absoluteUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri))
            return false;

        // A literal IP in the URL is checked directly; a hostname goes
        // through DNS first, and every resolved address must be safe - a
        // multi-homed hostname that resolves to even one private/internal
        // address is rejected, not just the first answer.
        if (IPAddress.TryParse(uri.Host, out var literalIp))
            return IsSafeAddress(literalIp);

        IPAddress[] addresses;
        try
        {
            addresses = await _dnsResolver.ResolveAsync(uri.Host, ct);
        }
        catch (SocketException)
        {
            // Unresolvable host - reject rather than treat "unknown" as "allowed."
            return false;
        }

        return addresses.Length > 0 && addresses.All(IsSafeAddress);
    }

    private static bool IsSafeAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.Equals(CloudMetadataAddress))
            return false;

        if (IPAddress.IsLoopback(address))
            return false;

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsSafeIPv4(address),
            AddressFamily.InterNetworkV6 => IsSafeIPv6(address),
            _ => false,
        };
    }

    private static bool IsSafeIPv4(IPAddress address)
    {
        var b = address.GetAddressBytes();

        if (b[0] == 10) return false;                            // 10.0.0.0/8
        if (b[0] == 172 && b[1] is >= 16 and <= 31) return false; // 172.16.0.0/12
        if (b[0] == 192 && b[1] == 168) return false;             // 192.168.0.0/16
        if (b[0] == 169 && b[1] == 254) return false;             // 169.254.0.0/16 (link-local)
        if (b[0] == 127) return false;                            // 127.0.0.0/8 (loopback)
        if (b[0] == 0) return false;                              // 0.0.0.0/8

        return true;
    }

    private static bool IsSafeIPv6(IPAddress address)
    {
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return false;

        var b = address.GetAddressBytes();
        if ((b[0] & 0xFE) == 0xFC) return false; // fc00::/7 (unique local)

        return true;
    }
}
