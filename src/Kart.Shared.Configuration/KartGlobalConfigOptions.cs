namespace Kart.Shared.Configuration;

/// <summary>Tunables for <c>AddKartGlobalConfig</c>; sensible defaults so most services need
/// pass nothing at all.</summary>
public sealed class KartGlobalConfigOptions
{
    /// <summary>
    /// Gitignored, per-developer JSON file layered in before <see cref="PathConfigurationKey"/>
    /// is read, so each machine can set its own path without touching a committed file. Never
    /// committed itself — only a <c>.example</c> template of it is.
    /// </summary>
    public string LocalOverrideFileName { get; set; } = "appsettings.Local.json";

    /// <summary>
    /// Configuration key holding the absolute path to the shared GlobalConfig file — a
    /// per-machine, gitignored JSON file holding every service's real local secrets (connection
    /// strings, signing keys, etc.) under its own <c>Services:&lt;serviceName&gt;</c> section,
    /// plus a <c>Global</c> section of platform-wide defaults every service inherits — layered
    /// on top of everything read so far. Which <c>Services</c> section applies is the
    /// <c>serviceName</c> argument passed to <c>AddKartGlobalConfig</c> directly, not an option
    /// here.
    /// </summary>
    public string PathConfigurationKey { get; set; } = "GlobalConfig:Path";
}
