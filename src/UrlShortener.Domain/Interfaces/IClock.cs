namespace UrlShortener.Domain.Interfaces;

/// <summary>Abstracts wall-clock time so domain/application logic is deterministically testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
