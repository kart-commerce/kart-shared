using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kart.Shared.ErrorHandling;

/// <summary>
/// The single place every exception reaching the HTTP boundary is logged and translated into
/// the platform's <see cref="KartProblemDetailsFactory"/> envelope, so handlers/controllers never
/// need their own try/catch (kart-conventions.md: "No local try/catch for translation").
///
/// Generalizes both reference services' <c>GlobalExceptionHandler</c>:
/// - kart-category-service special-cased FluentValidation's <c>ValidationException</c> (400) and
///   translated every other exception into a generic 500 Problem.
/// - kart-identity-service special-cased ~15 Application-layer exception types to specific status
///   codes/error codes via a <c>switch</c>, and returned <c>false</c> for anything unrecognized —
///   falling through to ASP.NET Core's own default handling.
///
/// This handler keeps both special-casing behaviors (FluentValidation handling, plus an
/// extensible <see cref="KartErrorHandlingOptions.Map{TException}"/> registry a service configures
/// with its own exception vocabulary) but resolves the one place they disagreed: an unrecognized
/// exception is ALWAYS translated to the platform's Problem envelope, never left to fall through.
/// kart-conventions.md's "Consistent response envelope, every service" rule is exactly why: two
/// services must never differ in what shape an unexpected 500 comes back as.
///
/// "One log per exception, not zero, not two" (kart-conventions.md) is preserved verbatim: every
/// branch below logs exactly once, at Warning for a recognized/expected rejection and at Error
/// for a genuine unhandled failure.
/// </summary>
public sealed class KartExceptionHandler(
    ILogger<KartExceptionHandler> logger,
    IOptions<KartErrorHandlingOptions> options) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var opts = options.Value;

        if (opts.HandleFluentValidationExceptions && exception is ValidationException validationException)
        {
            await WriteAsync(
                httpContext,
                StatusCodes.Status400BadRequest,
                "validation_error",
                "One or more validation errors occurred.",
                ToValidationDetails(validationException),
                exception,
                cancellationToken);
            return true;
        }

        if (opts.TryMap(exception, out var mapping))
        {
            await WriteAsync(
                httpContext,
                mapping.StatusCode,
                mapping.ErrorCode,
                mapping.DetailSelector(exception),
                details: null,
                exception,
                cancellationToken);
            return true;
        }

        // Not a recognized/expected exception type — a genuine unhandled failure. Logged at
        // Error (never Warning) since this always represents a bug or an unhandled
        // infrastructure fault, never an expected client-facing rejection. The full exception
        // (message, stack trace) only ever reaches the structured log, never the client.
        logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        var problem = KartProblemDetailsFactory.Create(
            httpContext,
            StatusCodes.Status500InternalServerError,
            "internal_error",
            "An unexpected error occurred.");

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private async Task WriteAsync(
        HttpContext httpContext,
        int statusCode,
        string errorCode,
        string detail,
        IReadOnlyDictionary<string, object?>? details,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            exception,
            "Request rejected with {ErrorCode} ({StatusCode}) for {Method} {Path}",
            errorCode,
            statusCode,
            httpContext.Request.Method,
            httpContext.Request.Path);

        var problem = KartProblemDetailsFactory.Create(httpContext, statusCode, errorCode, detail, details);
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
    }

    private static IReadOnlyDictionary<string, object?> ToValidationDetails(ValidationException exception) =>
        exception.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, object? (g) => g.Select(e => e.ErrorMessage).ToArray());
}
