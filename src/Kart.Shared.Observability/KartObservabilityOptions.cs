namespace Kart.Shared.Observability;

/// <summary>OTLP wire protocol — mirrors <c>OpenTelemetry.Exporter.OtlpExportProtocol</c> and
/// <c>Serilog.Sinks.OpenTelemetry.OtlpProtocol</c> without forcing a consumer to reference either
/// package just to configure <see cref="KartObservabilityOptions.DefaultOtlpProtocol"/>.</summary>
public enum KartOtlpProtocol
{
    /// <summary>Port 4317 on a standard OTel Collector. Lower overhead; the default everywhere on the platform.</summary>
    Grpc,

    /// <summary>Port 4318. Use when the collector sits behind an HTTP-only ingress/load balancer that can't proxy gRPC.</summary>
    HttpProtobuf,
}

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

    /// <summary>Configuration key read for the OTLP wire protocol (<c>"Grpc"</c> or <c>"HttpProtobuf"</c>). Falls back to <see cref="DefaultOtlpProtocol"/> when absent/unparseable.</summary>
    public string OtlpProtocolConfigurationKey { get; set; } = "Observability:Otlp:Protocol";

    /// <summary>Protocol used for every OTLP export (logs, traces, metrics) when <see cref="OtlpProtocolConfigurationKey"/> isn't set. gRPC (the Collector's default receiver) unless a service's network path can't carry it.</summary>
    public KartOtlpProtocol DefaultOtlpProtocol { get; set; } = KartOtlpProtocol.Grpc;

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

    /// <summary>
    /// Configuration key read for the trace sampling ratio (0.0-1.0). Backs a
    /// <c>ParentBasedSampler(TraceIdRatioBasedSampler(ratio))</c> — a sampled parent is always
    /// respected, so this only governs root-span sampling decisions. Falls back to
    /// <see cref="DefaultTracingSamplingRatio"/> when absent/unparseable.
    /// </summary>
    public string TracingSamplingRatioConfigurationKey { get; set; } = "Observability:Tracing:SamplingRatio";

    /// <summary>
    /// Root-span sampling ratio used when <see cref="TracingSamplingRatioConfigurationKey"/> isn't
    /// set. Defaults to <c>1.0</c> (sample everything) to match every service's behavior before
    /// this knob existed — at 1M req/s that is almost certainly too expensive for a
    /// non-100%-tier service, so tune it down (e.g. <c>0.1</c>) via config rather than lowering
    /// this default, which would be a silent behavior change for every existing consumer.
    /// Per <c>kart-conventions.md</c>, <c>kart-order-service</c>, <c>kart-inventory-service</c>,
    /// <c>kart-payment-service</c>, and <c>kart-shipping-service</c> (the Order Saga) must stay at
    /// <c>1.0</c>.
    /// </summary>
    public double DefaultTracingSamplingRatio { get; set; } = 1.0;

    /// <summary>
    /// Whether to expose a local <c>/metrics</c> Prometheus scrape (pull) endpoint alongside any
    /// configured OTLP (push) metrics export. Both can run at once — many platforms keep the pull
    /// endpoint as a scrape-free-standing fallback/for local dev even once OTLP push is wired.
    /// Set <c>false</c> once a service's Prometheus is fed exclusively via the Collector's
    /// <c>prometheusremotewrite</c> exporter, to avoid double-collecting every metric.
    /// </summary>
    public bool EnablePrometheusScrapeEndpoint { get; set; } = true;

    /// <summary>
    /// This service's <c>service.version</c> resource attribute (e.g. its informational/assembly
    /// version or a deployed image tag). Left unset by default — most services don't yet have a
    /// stable version source wired through config, and an absent attribute is preferable to a
    /// misleading one.
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// This service's <c>service.instance.id</c> resource attribute — what disambiguates one
    /// horizontally-scaled replica's logs/traces/metrics from another's in Grafana. Defaults to
    /// the <c>HOSTNAME</c> environment variable (the pod name under Kubernetes) or, failing that,
    /// the machine name — never a random GUID, so an operator can correlate a Tempo span straight
    /// back to the pod that produced it.
    /// </summary>
    public string ServiceInstanceId { get; set; } =
        Environment.GetEnvironmentVariable("HOSTNAME") is { Length: > 0 } hostName
            ? hostName
            : Environment.MachineName;

    /// <summary>
    /// Max in-memory span/log-record queue depth per OTLP batch exporter before new items are
    /// dropped. The OpenTelemetry .NET SDK default (2048) is tuned for modest traffic; raised here
    /// so a brief Collector hiccup doesn't drop spans under sustained high-TPS load. Costs memory
    /// per queued item — tune down on a memory-constrained service.
    /// </summary>
    public int OtlpMaxQueueSize { get; set; } = 8192;

    /// <summary>Max spans/log records sent per OTLP export batch. Raised (from the SDK default of 512) to keep export frequency reasonable at high throughput without growing <see cref="OtlpScheduledDelayMilliseconds"/>.</summary>
    public int OtlpMaxExportBatchSize { get; set; } = 2048;

    /// <summary>Delay between OTLP batch flushes, in milliseconds. Lowered (from the SDK default of 5000) so the export queue drains faster and the data-loss window on a crash stays small, trading a modest increase in export call volume.</summary>
    public int OtlpScheduledDelayMilliseconds { get; set; } = 2000;

    /// <summary>Interval between OTLP metric exports, in milliseconds. 15s balances Grafana dashboard freshness against Collector/network load — well below Prometheus's typical 15-60s scrape interval, so the pull and push paths (see <see cref="EnablePrometheusScrapeEndpoint"/>) stay roughly in sync.</summary>
    public int OtlpMetricsExportIntervalMilliseconds { get; set; } = 15_000;
}
