# Kart.Shared.Observability

Serilog + OpenTelemetry SDK wiring shared across Kart's microservices, per
`kart-conventions.md`'s Observability section: "one platform-wide implementation, not built
locally by each service."

## Architecture: one OTLP endpoint in, the Collector decides where each signal goes

Every signal this package produces — logs, traces, metrics — is exported over the **same OTLP
endpoint** (`Observability:Otlp:Endpoint`) to an OpenTelemetry Collector. A service never talks to
Loki, Tempo, or Prometheus directly (aside from the optional local `/metrics` scrape endpoint, see
below); routing each signal type to its backend is entirely the Collector's job, driven by its own
pipeline config:

```
                    ┌─────────────────────┐
   Serilog logs ───▶│                     │───▶ otlphttp exporter ──▶ Loki (OTLP-native ingest)
                     │   OTel Collector    │
  OTel SDK traces ──▶│  (receivers: otlp   │───▶ otlp exporter ──────▶ Tempo
                     │   grpc:4317/        │
  OTel SDK metrics ─▶│   http:4318)        │───▶ prometheus exporter ▶ Prometheus (scraped)
                     └─────────────────────┘
                                                  Grafana ── queries all three as datasources
```

This package's job stops at "get every signal to the Collector, correctly labeled, without falling
over at high throughput." The Collector/Loki/Tempo/Prometheus/Grafana stack that implements the
receiving side is **not owned here** — it's the shared local-dev stack in `kart-devops`
(`docker-compose.observability.yml`, see that repo's `observability/README.md`), per
`observability-standards.md`'s "owned once, centrally [in kart-devops], never copy-pasted per
repo." Point `Observability:Otlp:Endpoint` at that stack's Collector
(`http://localhost:4317`/`:4318` when it's running locally) and open Grafana at
`http://localhost:3000` — every signal this package exports shows up there with no per-service
Grafana setup. Each real environment's Collector is deployed the same way by `kart-infra`.

## What this package wires up

One call, `AddKartObservability(serviceName)`, on `WebApplicationBuilder`:

- **Serilog** — compact JSON to console in every environment except Development (shipping to Loki
  is the OpenTelemetry Collector's job, never something the process does directly); in
  Development, a plain templated console instead, since there's no collector tailing stdout there
  and a human is reading it directly. Enriched with `LogContext`, span/trace ids
  (`Serilog.Enrichers.Span`), a `Flow` property (`FlowEnricher`, only set when
  `KartFlowContext.Current` is), and `service`/`Service`/`service.instance.id` properties.
  Defaults `Microsoft`/`Microsoft.AspNetCore`/`System` to `Warning` and everything else to
  `Information` *before* reading a service's own `Serilog` appsettings section — so a service that
  never configured logging at all doesn't flood Loki with ASP.NET Core framework-internal noise at
  real traffic volume, while a service that *does* configure its own overrides still wins.
- **Rolling file log** (opt-in) — when `Observability:LogFile:Directory` (configurable) is set, an
  additional sink writes to `{directory}/{serviceName}-.log`, rolling daily and every 10 MB
  (both configurable via `KartObservabilityOptions`). This is a local dev convenience: the
  directory is expected to come from a per-machine source (e.g. this service's own GlobalConfig
  file via `Kart.Shared.Configuration`), never a value committed to source control. Unset by
  default — no file sink is added unless a directory is configured.
- **OTLP log export** — when `Observability:Otlp:Endpoint` is set, every log line is also shipped
  to the Collector (batched — see "High-throughput tuning" below), tagged with the same resource
  attributes (`service.name`, `service.instance.id`, `deployment.environment`, `service.version`)
  as traces/metrics, so a Loki query and a Tempo trace for the same request line up.
- **OpenTelemetry tracing** — ASP.NET Core, `HttpClient`, EF Core, raw Npgsql, and RabbitMQ
  (`Kart.Shared.Messaging.RabbitMqTraceContext`'s publish/consume spans) instrumentation; OTLP
  exporter when an endpoint is configured; a configurable sampling ratio (see below).
- **OpenTelemetry metrics** — ASP.NET Core, `HttpClient`, and .NET runtime instrumentation; a
  Prometheus scrape endpoint by default; OTLP exporter when configured. This is what gives every
  service the mandatory RED metrics (`observability-standards.md`) with no per-service setup.

## Usage

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddKartObservability("kart-category-service");

var app = builder.Build();
app.MapPrometheusScrapingEndpoint(); // exposes /metrics — skip if EnablePrometheusScrapeEndpoint is false
```

`serviceName` becomes both the OpenTelemetry resource name and the Serilog `service` enrichment
property — every service supplies its own, never hardcoded here.

### Configuration keys

| Key (default) | Purpose |
|---|---|
| `Observability:Otlp:Endpoint` | Collector endpoint (e.g. `http://otel-collector:4317`). Unset ⇒ no OTLP export at all; console/file/scrape-endpoint sinks still work. |
| `Observability:Otlp:Protocol` (`Grpc`) | `Grpc` or `HttpProtobuf` — set to `HttpProtobuf` when the network path to the Collector can't carry gRPC (e.g. behind an HTTP-only ingress). |
| `Observability:Tracing:SamplingRatio` (`1.0`) | Root-span sampling ratio, 0.0-1.0. A sampled parent is always honored regardless of this value — it only governs the root decision. See "Sampling" below. |
| `Observability:LogFile:Directory` (unset) | Local rolling-file sink directory; dev convenience only. |

Every key, plus the batch-tuning numbers below, is also settable in code via the `configure`
callback on `AddKartObservability` (`KartObservabilityOptions`) — configuration keys win when set,
so a service can ship sane code defaults and still let ops override them per-environment without a
redeploy.

### Sampling

Per `kart-conventions.md`, `kart-order-service`, `kart-inventory-service`,
`kart-payment-service`, and `kart-shipping-service` (the four services executing the Order Saga)
must sample traces at 100% — leave `Observability:Tracing:SamplingRatio` unset (it defaults to
`1.0`) on those four. Every other service should tune it down once real traffic makes 100%
capture too expensive for Tempo's storage/ingest — e.g. `0.1` (10%) is a reasonable starting point
at high TPS. This is a config change, not a code change: no redeploy needed to adjust it.

### High-throughput tuning

At sustained high request volume, the OpenTelemetry SDK's *default* batch-export settings
(2048 queue depth, 512-span batches, a 5s flush delay) can either buffer too little (dropping spans
under a brief Collector hiccup) or flush too infrequently (losing more in-flight data on a crash).
`KartObservabilityOptions` exposes the knobs directly — `OtlpMaxQueueSize` (default `8192`),
`OtlpMaxExportBatchSize` (default `2048`), `OtlpScheduledDelayMilliseconds` (default `2000`), and
`OtlpMetricsExportIntervalMilliseconds` (default `15000`) — applied consistently across the
Serilog OTLP sink and every OTel SDK OTLP exporter (traces, metrics). Tune further per service via
the `configure` callback if its traffic profile needs it.

### Resource attributes

Every exported signal (log, trace, metric) carries the same resource attributes, so they line up
in Grafana regardless of which backend they land in:

- `service.name` — the `serviceName` argument.
- `service.instance.id` — defaults to the `HOSTNAME` environment variable (the pod name under
  Kubernetes) or, failing that, the machine name. Deliberately never a random GUID, so an operator
  can trace a Tempo span straight back to the replica that produced it.
- `deployment.environment` — the ASP.NET Core hosting environment name (`Development`,
  `Staging`, `Production`, ...).
- `service.version` — only set when `KartObservabilityOptions.ServiceVersion` is explicitly
  configured; omitted otherwise rather than guessing.

### Metrics: pull and push aren't mutually exclusive

`EnablePrometheusScrapeEndpoint` (default `true`) keeps the local `/metrics` pull endpoint
running alongside any configured OTLP push export — many platforms keep the pull endpoint as a
scrape-based fallback (or for local dev without a Collector) even once OTLP push is wired. Set it
to `false` once a service's metrics are fed exclusively via the Collector's own
Prometheus-format exporter (what `kart-devops`'s local stack scrapes at `otel-collector:8889`), to
avoid collecting (and paying storage for) every metric twice.

## Verifying against the real stack

Start `kart-devops`'s stack (`docker compose -f docker-compose.observability.yml up -d` from that
repo) and point a service consuming this package at it
(`Observability:Otlp:Endpoint=http://localhost:4317`). Generate a little traffic, then open
Grafana at `http://localhost:3000` (admin/admin) → Explore: a log line in Loki
(`{service_name="<your service>"}`), its trace in Tempo (click through via the pre-wired
trace-to-logs correlation, or search by `service.name`), and its RED metrics in Prometheus
(`http_server_request_duration_seconds_count{...}`) should all be queryable within the Collector's
export interval (`OtlpMetricsExportIntervalMilliseconds`, 15s by default).

## Adoption status (as of 2026-08-14)

Generalized verbatim from kart-category-service's and kart-identity-service's own interim
`ObservabilityExtensions` — this package is a drop-in replacement for both. Adopted by
`kart-cart-service`, `kart-delivery-tracking-service`, `kart-offer-service`,
`kart-payment-service`, `kart-product-service`, `kart-search-service`, and `kart-user-service`;
`kart-category-service`, `kart-identity-service`, and `kart-inventory-service` are migrating onto
it. RabbitMQ publish/consume tracing (`Kart.Shared.Messaging.RabbitMqTraceContext`) closed the
previous "no message-bus instrumentation" gap — every consumer built on
`RabbitMqConsumerHostedServiceBase` gets a consume span automatically; a publisher calls
`RabbitMqTraceContext.StartPublishActivity`/`StartPublishActivityFromStoredTraceParent` explicitly
around its own `BasicPublish` call.
