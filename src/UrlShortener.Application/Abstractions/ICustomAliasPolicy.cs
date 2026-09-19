namespace UrlShortener.Application.Abstractions;

/// <summary>Encapsulates the reserved-word / profanity / format policy for user-supplied vanity aliases.</summary>
public interface ICustomAliasPolicy
{
    bool IsAllowed(string alias, out string? reason);
}
