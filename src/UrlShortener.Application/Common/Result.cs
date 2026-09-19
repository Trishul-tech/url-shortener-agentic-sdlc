namespace UrlShortener.Application.Common;

/// <summary>
/// Explicit success/failure wrapper for application-layer outcomes, so
/// handlers don't rely on exceptions for expected failure paths (e.g.
/// "code already taken") while domain invariant violations still throw.
/// </summary>
public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public string? ErrorCode { get; }

    private Result(bool isSuccess, T? value, string? error, string? errorCode)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        ErrorCode = errorCode;
    }

    public static Result<T> Success(T value) => new(true, value, null, null);
    public static Result<T> Failure(string error, string errorCode) => new(false, default, error, errorCode);
}

public static class ErrorCodes
{
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string Expired = "EXPIRED";
    public const string Deactivated = "DEACTIVATED";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string RateLimited = "RATE_LIMITED";
}
