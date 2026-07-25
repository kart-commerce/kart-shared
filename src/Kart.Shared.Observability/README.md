# Kart.Shared.Observability

Serilog + OpenTelemetry SDK wiring shared across Kart's microservices, per
`kart-conventions.md`'s Observability section: "one platform-wide implementation, not built
locally by each service."

## What this package wires up

One call, `AddKartObservability(serviceName)`, on `WebApplicationBuilder`:

- **Serilog** — structured JSON to console (shipping to Loki is the OpenTelemetry Collector's job,
  never something the process does directly), enriched with `LogContext`, span/trace ids
  (`Serilog.Enrichers.Span`), and a `service` property.
- **OpenTelemetry tracing** — ASP.NET Core, `HttpClient`, EF Core, and raw Npgsql instrumentation;
  OTLP exporter when `Observability:Otlp:Endpoint` (configurable) is set.
- **OpenTelemetry metrics** — ASP.NET Core, `HttpClient`, and .NET runtime instrumentation; a
  Prometheus scrape endpoint always; OTLP exporter when configured. This is what gives every
  service the mandatory RED metrics (`observability-standards.md`) with no per-service setup.

## Usage

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddKartObservability("kart-category-service");

var app = builder.Build();
app.MapPrometheusScrapingEndpoint(); // exposes /metrics
```

`serviceName` becomes both the OpenTelemetry resource name and the Serilog `service` enrichment
property — every service supplies its own, never hardcoded here.

### 100%-trace-coverage services

Per `kart-conventions.md`, `kart-order-service`, `kart-inventory-service`, `kart-payment-service`,
and `kart-shipping-service` (the four services executing the Order Saga) must sample traces at
100%; every other service uses the OpenTelemetry SDK's default sampler. `KartObservabilityOptions`
does not yet expose a sampling-tier knob — until it does, a service on the 100%-tier must configure
its own `Sampler` (e.g. `TracerProviderBuilder.SetSampler(new AlwaysOnSampler())`) after calling
`AddKartObservability`, or via `configure` once this package grows that option.

## Adoption status (as of 2026-07-25)

Generalized verbatim from kart-category-service's and kart-identity-service's own
(byte-for-byte identical, aside from `ServiceName`) interim `ObservabilityExtensions` — this
package is a drop-in replacement for both; neither has migrated to it yet.

## Known gap

Message-bus (RabbitMQ) publish/consume instrumentation is **not yet wired here** — only
ASP.NET Core, `HttpClient`, EF Core, and raw Npgsql are instrumented, matching what both reference
services had before this package existed. `observability-standards.md`'s "the message-bus client's
own instrumentation" auto-instruments inbound HTTP, outbound `HttpClient`, and database calls, but
a service publishing/consuming RabbitMQ messages must currently add its own `ActivitySource`
registration (via `tracing.AddSource("<own-source-name>")`) for message-publish spans and manual
W3C Trace Context propagation on message headers — this is the concrete next gap to close in this
package once a first service needs it.
