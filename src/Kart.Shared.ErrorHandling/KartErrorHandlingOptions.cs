namespace Kart.Shared.ErrorHandling;

/// <summary>
/// Per-service exception → HTTP response mapping, registered once at startup via
/// <c>AddKartErrorHandling</c>. Generalizes kart-identity-service's <c>GlobalExceptionHandler</c>
/// (a hand-written <c>switch</c> over ~15 Application-layer exception types) into a fluent
/// registration API any service can populate with its own exception vocabulary, while
/// kart-category-service's simpler case (only FluentValidation's <see cref="global::FluentValidation.ValidationException"/>
/// plus a generic fallback) is exactly what you get by registering nothing extra.
/// </summary>
public sealed class KartErrorHandlingOptions
{
    private readonly Dictionary<Type, ExceptionMapping> _mappings = new();

    /// <summary>
    /// FluentValidation's <c>ValidationException</c> → 400 <c>validation_error</c>, grouped by
    /// property name. Both reference services handle this identically; on by default.
    /// </summary>
    public bool HandleFluentValidationExceptions { get; set; } = true;

    /// <summary>
    /// Registers how a specific exception type (and, by walking the base-type chain, any
    /// subclass not itself registered) maps to an HTTP status code and platform error code.
    /// </summary>
    public KartErrorHandlingOptions Map<TException>(
        int statusCode,
        string errorCode,
        Func<TException, string>? detailSelector = null)
        where TException : Exception
    {
        _mappings[typeof(TException)] = new ExceptionMapping(
            statusCode,
            errorCode,
            ex => detailSelector is not null ? detailSelector((TException)ex) : ex.Message);

        return this;
    }

    internal bool TryMap(Exception exception, out ExceptionMapping mapping)
    {
        // Exact type first, then walk up the inheritance chain — lets a service register a
        // common base exception once instead of every subclass individually.
        for (var type = exception.GetType(); type is not null; type = type.BaseType)
        {
            if (_mappings.TryGetValue(type, out var found))
            {
                mapping = found;
                return true;
            }
        }

        mapping = default!;
        return false;
    }

    internal sealed record ExceptionMapping(int StatusCode, string ErrorCode, Func<Exception, string> DetailSelector);
}
