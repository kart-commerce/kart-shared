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
}
