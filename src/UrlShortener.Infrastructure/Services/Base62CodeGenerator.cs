using System.Security.Cryptography;
using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.ValueObjects;

namespace UrlShortener.Infrastructure.Services;

/// <summary>
/// Generates cryptographically random Base62 codes. Random (not sequential
/// counter-based) codes were chosen deliberately: sequential IDs let a
/// caller enumerate every short URL in the system, which is an information
/// disclosure risk for a public redirect service.
/// </summary>
public sealed class Base62CodeGenerator : ICodeGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public ShortCode GenerateCandidate(int length = 7)
    {
        Span<char> buffer = stackalloc char[length];
        Span<byte> randomBytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(randomBytes);

        for (var i = 0; i < length; i++)
            buffer[i] = Alphabet[randomBytes[i] % Alphabet.Length];

        return ShortCode.Create(new string(buffer));
    }
}
