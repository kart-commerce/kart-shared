using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Kart.Shared.Configuration;

/// <summary>
/// PLATFORM_BLUEPRINT.md's Configuration Management "Env" layer for local dev: every machine
/// points at its own GlobalConfig file (a per-machine, gitignored JSON file holding real local
/// secrets) via a gitignored local override, instead of any one developer's absolute path ever
/// landing in a committed <c>appsettings.*.json</c>. Generalized verbatim from
/// kart-identity-service's own interim <c>Program.cs</c> bootstrap.
/// </summary>
public static class GlobalConfigExtensions
{
    /// <summary>
    /// Layers <see cref="KartGlobalConfigOptions.LocalOverrideFileName"/> (optional, gitignored)
    /// on top of whatever configuration has been read so far, then resolves
    /// <see cref="KartGlobalConfigOptions.PathConfigurationKey"/> and layers that file on top too
    /// — throwing with an actionable message if the path is missing, since the app cannot start
    /// without its secrets. Call this as early as possible in <c>Program.cs</c>, before any code
    /// that depends on configuration the GlobalConfig file supplies (connection strings, signing
    /// keys, this package's own log file directory setting, etc.).
    /// </summary>
    public static WebApplicationBuilder AddKartGlobalConfig(
        this WebApplicationBuilder builder,
        Action<KartGlobalConfigOptions>? configure = null)
    {
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
                "and point it at your local GlobalConfig secrets file.");
        }

        builder.Configuration.AddJsonFile(globalConfigPath, optional: false, reloadOnChange: true);

        return builder;
    }
}
