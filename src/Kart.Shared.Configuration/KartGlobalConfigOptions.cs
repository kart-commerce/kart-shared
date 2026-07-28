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
    /// Configuration key holding the absolute path to this service's GlobalConfig file — a
    /// per-machine, gitignored JSON file holding real local secrets (connection strings,
    /// signing keys, etc.), layered on top of everything read so far.
    /// </summary>
    public string PathConfigurationKey { get; set; } = "GlobalConfig:Path";
}
