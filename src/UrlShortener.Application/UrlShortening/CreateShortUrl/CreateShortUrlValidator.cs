using FluentValidation;

namespace UrlShortener.Application.UrlShortening.CreateShortUrl;

public sealed class CreateShortUrlValidator : AbstractValidator<CreateShortUrlCommand>
{
    public CreateShortUrlValidator()
    {
        RuleFor(x => x.TargetUrl)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(BeAbsoluteHttpUrl).WithMessage("TargetUrl must be an absolute http(s) URL.");

        RuleFor(x => x.CustomAlias)
            .Matches("^[A-Za-z0-9_-]{3,32}$")
            .When(x => !string.IsNullOrEmpty(x.CustomAlias))
            .WithMessage("CustomAlias must be 3-32 alphanumeric/underscore/hyphen characters.");

        RuleFor(x => x.ExpiresAtUtc)
            .Must(exp => exp is null || exp > DateTimeOffset.UtcNow)
            .WithMessage("ExpiresAtUtc must be in the future.");
    }

    private static bool BeAbsoluteHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
