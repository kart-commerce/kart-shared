using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Kart.Shared.ErrorHandling;

/// <summary>
/// Builds the platform's RFC 7807 <see cref="ProblemDetails"/> envelope with the two extension
/// members kart-conventions.md's Error Handling section mandates on every one of Kart's services:
/// <c>traceId</c> (the current OpenTelemetry/Activity trace id, falling back to
/// <see cref="HttpContext.TraceIdentifier"/> if there is no active span) and <c>errorCode</c>
/// (the stable, machine-readable code every api-contract.yaml's Problem.code already uses —
/// generalized from kart-category-service's <c>ProblemDto</c> and kart-identity-service's
/// <c>Problem</c> records, which were otherwise identical in shape).
/// </summary>
public static class KartProblemDetailsFactory
{
    public static ProblemDetails Create(
        HttpContext httpContext,
        int statusCode,
        string errorCode,
        string detail,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonPhrases.GetReasonPhrase(statusCode),
            Detail = detail,
            Instance = httpContext.Request.Path,
        };

        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (details is { Count: > 0 })
        {
            problem.Extensions["details"] = details;
        }

        return problem;
    }
}
