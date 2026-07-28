namespace Kart.Shared.Observability;

/// <summary>Tunables for <c>AddKartObservability</c>; sensible defaults match both reference
/// services' current interim wiring, so most services need pass nothing at all.</summary>
public sealed class KartObservabilityOptions
{
    /// <summary>
    /// Configuration key read for the OTLP exporter endpoint. When absent/blank, the OTLP
    /// exporters are simply not added — traces/metrics still flow to the console sink and the
    /// Prometheus scrape endpoint, matching both services' current behavior in local dev.
    /// </summary>
    public string OtlpEndpointConfigurationKey { get; set; } = "Observability:Otlp:Endpoint";

    /// <summary>
    /// Configuration key read for a local rolling-file log directory. When absent/blank
    /// (the default), no file sink is added — logging stays console-only, e.g. in any
    /// environment shipping logs to Loki via the OTel Collector instead. This is a local dev
    /// convenience, so the directory itself is expected to come from a per-machine source
    /// (e.g. a service's own GlobalConfig file), never a value committed to source control.
    /// </summary>
    public string LogFileDirectoryConfigurationKey { get; set; } = "Observability:LogFile:Directory";

    /// <summary>Size at which the file sink rolls to a new file, in addition to rolling daily.</summary>
    public long LogFileSizeLimitBytes { get; set; } = 10 * 1024 * 1024;
}
