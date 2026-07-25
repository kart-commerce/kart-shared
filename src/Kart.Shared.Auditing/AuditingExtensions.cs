using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kart.Shared.Auditing;

/// <summary>One DI registration call per service, matching every other Kart.Shared.* package.</summary>
public static class AuditingExtensions
{
    /// <summary>Registers <see cref="NullAuditLogWriter"/> as a safe default. Prefer the generic
    /// overload once the service has a real audit sink to write to.</summary>
    public static IServiceCollection AddKartAuditing(this IServiceCollection services)
    {
        services.TryAddScoped<IAuditLogWriter, NullAuditLogWriter>();
        return services;
    }

    /// <summary>Registers <typeparamref name="TWriter"/> as this service's <see cref="IAuditLogWriter"/>.</summary>
    public static IServiceCollection AddKartAuditing<TWriter>(this IServiceCollection services)
        where TWriter : class, IAuditLogWriter
    {
        services.AddScoped<IAuditLogWriter, TWriter>();
        return services;
    }
}
