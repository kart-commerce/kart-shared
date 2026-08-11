using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

namespace Kart.Shared.Observability;

/// <summary>
/// observability-standards.md's mandated stack (Serilog -> Loki, OpenTelemetry -> Tempo/
/// Prometheus) behind the one DI registration call kart-conventions.md's Observability section
/// requires: <c>Kart.Shared.Observability</c>, "wires Serilog + the OpenTelemetry SDK (ASP.NET
/// Core/HttpClient/Npgsql/EF Core instrumentation, OTLP exporter) with one DI registration call
/// per service". Generalized verbatim from kart-category-service's and kart-identity-service's
/// own (byte-for-byte identical, aside from ServiceName) interim <c>ObservabilityExtensions</c> —
/// this is a drop-in replacement for both.
/// </summary>
public static class ObservabilityExtensions
{
    // Human-readable — used in Development on both the console and (when configured) the file
    // sink, since neither has a collector tailing it for a human to instead read compact JSON.
    private const string DevelopmentOutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Wires Serilog (compact JSON to console in every environment but Development, where a
    /// human is reading stdout directly instead of a collector — a plain templated console
    /// there instead; an additional rolling-file sink when configured) and the OpenTelemetry SDK
    /// (ASP.NET Core, HttpClient, EF Core, and raw Npgsql tracing; ASP.NET Core, HttpClient, and
    /// runtime metrics; OTLP exporter when an endpoint is configured; Prometheus scrape endpoint
    /// always).
    /// </summary>
    /// <param name="serviceName">
    /// This service's OpenTelemetry resource name and Serilog "service" enrichment property —
    /// e.g. "kart-category-service". Each service supplies its own; never hardcoded here.
    /// </param>
    public static WebApplicationBuilder AddKartObservability(
        this WebApplicationBuilder builder,
        string serviceName,
        Action<KartObservabilityOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var options = new KartObservabilityOptions();
        configure?.Invoke(options);

        var otlpEndpoint = builder.Configuration[options.OtlpEndpointConfigurationKey];

        builder.Host.UseSerilog((context, services, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithSpan()
                .Enrich.With<FlowEnricher>()
                .Enrich.WithProperty("service", serviceName)
                // Capitalized alias alongside the existing lowercase "service" property (kept for
                // back-compat with anything already querying it) — the platform-wide business-
                // flow tracing standard's mandatory field set is Timestamp/Service/Flow/TraceId/
                // SpanId/Stage/Level/Message; Timestamp/Level/Message come from Serilog itself,
                // TraceId/SpanId from .Enrich.WithSpan() above, Flow from FlowEnricher, and Stage
                // is passed explicitly per log call (it changes line-to-line, unlike Flow).
                .Enrich.WithProperty("Service", serviceName);

            var isDevelopment = context.HostingEnvironment.IsDevelopment();

            if (isDevelopment)
            {
                loggerConfiguration.WriteTo.Console(outputTemplate: DevelopmentOutputTemplate);
            }
            else
            {
                loggerConfiguration.WriteTo.Console(new CompactJsonFormatter());
            }

            // Closes this package's own previously-documented gap: the Console/File sinks above
            // are for a human (or a local `tail`) reading stdout directly — neither one actually
            // ships a log line to the Collector. Every log line reaching Loki, correlated by
            // TraceId with its Tempo span, depends on this sink existing.
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                loggerConfiguration.WriteTo.OpenTelemetry(otlpOptions =>
                {
                    otlpOptions.Endpoint = otlpEndpoint;
                    otlpOptions.Protocol = OtlpProtocol.Grpc;
                    otlpOptions.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = serviceName,
                    };
                });
            }

            var logFileDirectory = context.Configuration[options.LogFileDirectoryConfigurationKey];
            if (!string.IsNullOrWhiteSpace(logFileDirectory))
            {
                var logFilePath = Path.Combine(logFileDirectory, $"{serviceName}-.log");

                if (isDevelopment)
                {
                    loggerConfiguration.WriteTo.File(
                        logFilePath,
                        outputTemplate: DevelopmentOutputTemplate,
                        rollingInterval: RollingInterval.Day,
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: options.LogFileSizeLimitBytes);
                }
                else
                {
                    loggerConfiguration.WriteTo.File(
                        new CompactJsonFormatter(),
                        logFilePath,
                        rollingInterval: RollingInterval.Day,
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: options.LogFileSizeLimitBytes);
                }
            }
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    // Npgsql emits its own ActivitySource ("Npgsql") natively since v6+ — no
                    // separate instrumentation package needed, just opt the tracer into it.
                    .AddSource("Npgsql")
                    // Kart.Shared.Messaging.RabbitMqTraceContext's publish/consume spans — OTel
                    // has no built-in RabbitMQ instrumentation, so every service using that
                    // helper needs its ActivitySource opted into the tracer the same way Npgsql's
                    // is above, or its spans are created but never exported.
                    .AddSource("Kart.Shared.Messaging.RabbitMq");

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                // RED metrics (rate/errors/duration) on every HTTP endpoint, per
                // observability-standards.md — ASP.NET Core's own instrumentation already
                // emits http.server.request.duration; scraped at /metrics.
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddPrometheusExporter();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return builder;
    }
}
