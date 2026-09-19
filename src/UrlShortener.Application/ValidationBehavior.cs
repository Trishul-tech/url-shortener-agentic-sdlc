using FluentValidation;
using MediatR;
using UrlShortener.Application.Common;

namespace UrlShortener.Application;

/// <summary>
/// MediatR pipeline behavior that runs FluentValidation before a handler
/// executes. For requests whose response type is Result&lt;T&gt;, validation
/// failures are returned as a typed Failure instead of throwing, keeping
/// the API layer's error handling uniform.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any())
            return await next();

        var failures = (await Task.WhenAll(_validators.Select(v => v.ValidateAsync(request, ct))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
            return await next();

        var message = string.Join(" | ", failures.Select(f => f.ErrorMessage));

        var responseType = typeof(TResponse);
        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = responseType.GetGenericArguments()[0];
            var failureMethod = responseType.GetMethod(nameof(Result<object>.Failure))!;
            return (TResponse)failureMethod.Invoke(null, new object[] { message, ErrorCodes.ValidationFailed })!;
        }

        throw new ValidationException(failures);
    }
}
