using UrlShortener.Application.Abstractions;

namespace UrlShortener.Infrastructure.Services;

public sealed class DefaultCustomAliasPolicy : ICustomAliasPolicy
{
    private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "admin", "health", "healthz", "swagger", "static", "assets", "login", "logout",
        "signup", "root", "www", "null", "undefined", "favicon"
    };

    public bool IsAllowed(string alias, out string? reason)
    {
        if (ReservedWords.Contains(alias))
        {
            reason = $"'{alias}' is a reserved word.";
            return false;
        }

        reason = null;
        return true;
    }
}
