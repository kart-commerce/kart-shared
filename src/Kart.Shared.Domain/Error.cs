namespace Kart.Shared.Domain;

/// <summary>
/// Domain/business error, returned via <see cref="Result"/> rather than thrown
/// (agent-reusables' api-standards.md: "Domain/business errors use a Result/Either pattern —
/// not exceptions"). <see cref="Code"/> is the platform-wide error-code vocabulary a service's
/// api-contract.yaml Problem.code should reuse where it applies. Only the truly generic codes
/// live here (validation/not-found/conflict/unauthorized) — a service-specific code (e.g.
/// kart-category-service's "max_depth_exceeded") is NOT something this package should grow to
/// include; use <see cref="Custom"/> for those from the owning service's own code.
/// </summary>
public sealed class Error
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public string Code { get; }
    public string Message { get; }

    private Error(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public static Error Validation(string message) => new("validation_error", message);

    public static Error NotFound(string message) => new("not_found", message);

    public static Error Conflict(string message) => new("conflict", message);

    public static Error Unauthorized(string message) => new("unauthorized", message);

    /// <summary>Escape hatch for a service's own domain-specific error codes.</summary>
    public static Error Custom(string code, string message) => new(code, message);
}
