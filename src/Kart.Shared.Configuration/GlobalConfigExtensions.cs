using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Kart.Shared.Configuration;

/// <summary>
/// PLATFORM_BLUEPRINT.md's Configuration Management "GlobalConfig" layer: one shared,
/// per-machine, gitignored JSON file every service points at via a gitignored local override
/// (<see cref="KartGlobalConfigOptions.LocalOverrideFileName"/>), instead of any one developer's
/// absolute path ever landing in a committed <c>appsettings.*.json</c>. The file itself holds a
/// <c>Global</c> section (platform-wide defaults every service inherits) and a
/// <c>Services:&lt;serviceName&gt;</c> section per service (that service's own secrets) — see
/// <see cref="GlobalConfigConfigurationProvider"/> for how the two are merged. Generalized
/// verbatim from kart-identity-service's own interim <c>Program.cs</c> bootstrap.
/// </summary>
public static class GlobalConfigExtensions
{
    /// <summary>
    /// Layers <see cref="KartGlobalConfigOptions.LocalOverrideFileName"/> (optional, gitignored)
    /// on top of whatever configuration has been read so far, then resolves
    /// <see cref="KartGlobalConfigOptions.PathConfigurationKey"/> and layers that file's
    /// <c>Global</c> + <c>Services:&lt;serviceName&gt;</c> sections on top too — throwing with an
    /// actionable message if the path is missing, since the app cannot start without its
    /// secrets. Call this as early as possible in <c>Program.cs</c>, before any code that depends
    /// on configuration the GlobalConfig file supplies (connection strings, signing keys, this
    /// package's own log file directory setting, etc.).
    /// </summary>
    /// <param name="serviceName">
    /// This service's canonical name (e.g. <c>"kart-product-service"</c>) — the same literal
    /// already passed to <c>AddKartObservability</c> right after this call. Selects which
    /// <c>Services:&lt;serviceName&gt;</c> block of the shared GlobalConfig file applies, and is
    /// used to compute this service's default log directory
    /// (<c>{Global:LogRoot}/{serviceName}</c>). There's no reliable way to infer it automatically
    /// — assembly names don't consistently match repo names (<c>Kart.Product.Api</c> vs.
    /// <c>KartAdminService.Api</c> vs. <c>Kart.Identity.Api</c>) — so it's a required argument
    /// rather than a <see cref="KartGlobalConfigOptions"/> default.
    /// </param>
    public static WebApplicationBuilder AddKartGlobalConfig(
        this WebApplicationBuilder builder,
        string serviceName,
        Action<KartGlobalConfigOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException(
                "serviceName must be a non-empty service name (e.g. \"kart-product-service\").",
                nameof(serviceName));
        }

        var options = new KartGlobalConfigOptions();
        configure?.Invoke(options);

        builder.Configuration.AddJsonFile(
            options.LocalOverrideFileName, optional: true, reloadOnChange: true);

        var globalConfigPath = builder.Configuration[options.PathConfigurationKey];
        if (string.IsNullOrWhiteSpace(globalConfigPath))
        {
            throw new InvalidOperationException(
                $"{options.PathConfigurationKey} is not set — copy " +
                $"{options.LocalOverrideFileName}.example to {options.LocalOverrideFileName} " +
                "and point it at the shared GlobalConfig secrets file.");
        }

        builder.Configuration.Add<GlobalConfigConfigurationSource>(source =>
        {
            source.ServiceName = serviceName;
            source.Path = globalConfigPath;
            source.Optional = false;
            source.ReloadOnChange = true;
            // globalConfigPath is always absolute (it names a file outside the repo, on this
            // machine) — without this, the base FileConfigurationSource resolves it against the
            // default file provider (rooted at the app's content root) instead of the filesystem
            // root, the same resolution AddJsonFile(path, ...) does internally for the same reason.
            source.ResolveFileProvider();
        });

        return builder;
    }
}
