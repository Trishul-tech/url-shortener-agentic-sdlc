using System.Text.RegularExpressions;
using UrlShortener.Domain.Exceptions;

namespace UrlShortener.Domain.ValueObjects;

/// <summary>
/// A validated Base62 short code (6-10 chars). Value object: two ShortCodes
/// with the same text are equal, and an invalid code can never be constructed.
/// </summary>
public sealed partial class ShortCode : IEquatable<ShortCode>
{
    public const int MinLength = 4;
    public const int MaxLength = 12;

    public string Value { get; }

    private ShortCode(string value) => Value = value;

    public static ShortCode Create(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            throw new InvalidTargetUrlException(candidate ?? string.Empty, "short code must not be empty");

        if (candidate.Length is < MinLength or > MaxLength)
            throw new InvalidTargetUrlException(candidate, $"short code length must be between {MinLength} and {MaxLength}");

        if (!AllowedPattern().IsMatch(candidate))
            throw new InvalidTargetUrlException(candidate, "short code must be alphanumeric (Base62)");

        return new ShortCode(candidate);
    }

    /// <summary>Bypasses validation for values already known-good (e.g. loaded from the database).</summary>
    public static ShortCode FromTrusted(string value) => new(value);

    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex AllowedPattern();

    public bool Equals(ShortCode? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => Equals(obj as ShortCode);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value;
}
