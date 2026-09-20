using System.Net;
using FluentAssertions;
using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Security;
using Xunit;

namespace UrlShortener.Application.Tests;

public class SsrfGuardTests
{
    private sealed class FakeDnsResolver : IDnsResolver
    {
        private readonly IPAddress[] _addresses;
        public FakeDnsResolver(params string[] ips) => _addresses = ips.Select(IPAddress.Parse).ToArray();
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Task.FromResult(_addresses);
    }

    [Theory]
    [InlineData("http://93.184.216.34/page")]
    [InlineData("https://8.8.8.8/")]
    public async Task IsSafePublicTargetAsync_WithPublicIpLiteral_ReturnsTrue(string url)
    {
        var guard = new SsrfGuard(new FakeDnsResolver());
        (await guard.IsSafePublicTargetAsync(url, CancellationToken.None)).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://172.16.5.5/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://0.0.0.0/")]
    public async Task IsSafePublicTargetAsync_WithPrivateOrInternalIpLiteral_ReturnsFalse(string url)
    {
        var guard = new SsrfGuard(new FakeDnsResolver());
        (await guard.IsSafePublicTargetAsync(url, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafePublicTargetAsync_WithHostnameResolvingToPublicIp_ReturnsTrue()
    {
        var guard = new SsrfGuard(new FakeDnsResolver("93.184.216.34"));
        (await guard.IsSafePublicTargetAsync("https://example.com/page", CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task IsSafePublicTargetAsync_WithHostnameResolvingToPrivateIp_ReturnsFalse()
    {
        var guard = new SsrfGuard(new FakeDnsResolver("10.0.0.5"));
        (await guard.IsSafePublicTargetAsync("https://internal.example.com/", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafePublicTargetAsync_WithMultiHomedHostnameWhereOneAddressIsPrivate_ReturnsFalse()
    {
        var guard = new SsrfGuard(new FakeDnsResolver("93.184.216.34", "10.0.0.5"));
        (await guard.IsSafePublicTargetAsync("https://multi.example.com/", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task IsSafePublicTargetAsync_WithNonAbsoluteUrl_ReturnsFalse()
    {
        var guard = new SsrfGuard(new FakeDnsResolver());
        (await guard.IsSafePublicTargetAsync("not-a-url", CancellationToken.None)).Should().BeFalse();
    }
}
