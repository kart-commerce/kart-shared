using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Kart.Shared.Messaging;

/// <summary>
/// DI helpers for the mechanical parts of RabbitMQ wiring every Kart service repeats: loading the
/// manifest, building a connection factory, declaring topology at startup. A service still binds
/// its own options type and still registers its own publishers/consumers — those two are
/// deliberately not generic across services.
/// </summary>
public static class KartMessagingServiceCollectionExtensions
{
    /// <summary>
    /// Loads and registers a service's own <see cref="MessageBusManifest"/> as a singleton, lazily
    /// (only once actually resolved) since <paramref name="manifestPathFactory"/> typically reads
    /// a service's own bound options. A relative path resolves against <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public static IServiceCollection AddKartMessageBusManifest(this IServiceCollection services, Func<IServiceProvider, string> manifestPathFactory)
    {
        return services.AddSingleton(sp =>
        {
            var manifestPath = manifestPathFactory(sp);
            var resolvedPath = Path.IsPathRooted(manifestPath)
                ? manifestPath
                : Path.Combine(AppContext.BaseDirectory, manifestPath);
            return MessageBusManifestLoader.Load(resolvedPath);
        });
    }

    /// <summary>
    /// Registers <see cref="IConnectionFactory"/> as a singleton built from <paramref name="settingsFactory"/>.
    /// Building the factory does not connect eagerly, so this is safe to register even if RabbitMQ
    /// is unreachable at startup — the topology/publisher/consumer hosted services each own their
    /// own retrying connection.
    /// </summary>
    public static IServiceCollection AddKartRabbitMqConnectionFactory(
        this IServiceCollection services,
        Func<IServiceProvider, RabbitMqConnectionSettings> settingsFactory)
    {
        return services.AddSingleton<IConnectionFactory>(sp =>
        {
            var settings = settingsFactory(sp);
            var factory = new ConnectionFactory
            {
                HostName = settings.HostName,
                Port = settings.Port,
                DispatchConsumersAsync = true,
            };
            if (!string.IsNullOrEmpty(settings.UserName))
            {
                factory.UserName = settings.UserName;
                factory.Password = settings.Password;
            }

            return factory;
        });
    }

    /// <summary>Registers <see cref="RabbitMqTopologyStartupHostedService"/>. Requires <see cref="IConnectionFactory"/> and <see cref="MessageBusManifest"/> already registered.</summary>
    public static IServiceCollection AddKartRabbitMqTopologyStartup(this IServiceCollection services)
    {
        return services.AddHostedService<RabbitMqTopologyStartupHostedService>();
    }
}
