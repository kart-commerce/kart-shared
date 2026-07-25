using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kart.Shared.ErrorHandling;

/// <summary>
/// One DI registration call per service (kart-conventions.md's "one platform-wide
/// implementation, not built locally by each service" pattern) plus the one middleware call
/// needed to activate it.
/// </summary>
public static class ErrorHandlingExtensions
{
    /// <summary>
    /// Registers <see cref="KartExceptionHandler"/> and ASP.NET Core's own ProblemDetails
    /// service. Call <see cref="UseKartErrorHandling"/> in the request pipeline to activate it.
    /// </summary>
    public static IServiceCollection AddKartErrorHandling(
        this IServiceCollection services,
        Action<KartErrorHandlingOptions>? configure = null)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<KartExceptionHandler>();

        var options = new KartErrorHandlingOptions();
        configure?.Invoke(options);
        services.AddSingleton(Options.Create(options));

        return services;
    }

    /// <summary>Activates the exception handler registered by <see cref="AddKartErrorHandling"/>.</summary>
    public static IApplicationBuilder UseKartErrorHandling(this IApplicationBuilder app) =>
        app.UseExceptionHandler();
}
