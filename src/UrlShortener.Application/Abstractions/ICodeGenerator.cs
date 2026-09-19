using UrlShortener.Domain.ValueObjects;

namespace UrlShortener.Application.Abstractions;

/// <summary>Generates candidate short codes. Infrastructure decides the algorithm (random Base62 vs. counter-based).</summary>
public interface ICodeGenerator
{
    ShortCode GenerateCandidate(int length = 7);
}
