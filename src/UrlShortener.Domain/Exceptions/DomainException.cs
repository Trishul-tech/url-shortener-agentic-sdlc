namespace UrlShortener.Domain.Exceptions;

/// <summary>
/// Base type for all domain-level rule violations. Kept distinct from
/// infrastructure/validation exceptions so the API layer can map them to a
/// stable set of HTTP problem types without inspecting message text.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public sealed class ShortCodeCollisionException : DomainException
{
    public ShortCodeCollisionException(string code)
        : base($"Short code '{code}' is already in use.") { }
}

public sealed class InvalidTargetUrlException : DomainException
{
    public InvalidTargetUrlException(string url, string reason)
        : base($"Target URL '{url}' is invalid: {reason}") { }
}

public sealed class ShortUrlNotFoundException : DomainException
{
    public ShortUrlNotFoundException(string code)
        : base($"Short URL with code '{code}' was not found.") { }
}

public sealed class ShortUrlExpiredException : DomainException
{
    public ShortUrlExpiredException(string code)
        : base($"Short URL with code '{code}' has expired.") { }
}

public sealed class ShortUrlDeactivatedException : DomainException
{
    public ShortUrlDeactivatedException(string code)
        : base($"Short URL with code '{code}' has been deactivated.") { }
}
