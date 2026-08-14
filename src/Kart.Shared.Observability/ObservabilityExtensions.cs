using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

namespace Kart.Shared.Observability;

/// <summary>
/// observability-standards.md's mandated stack (Serilog -> Loki, OpenTelemetry -> Tempo/
/// Prometheus) behind the one DI registration call kart-conventions.md's Observability section
/// requires: <c>Kart.Shared.Observability</c>, "wires Serilog + the OpenTelemetry SDK (ASP.NET
/// Core/HttpClient/Npgsql/EF Core instrumentation, OTLP exporter) with one DI registration call
/// per service". Every signal (logs, traces, metrics) is exported over OTLP to a single Collector
/// endpoint; routing each signal type to its backend (Loki/Tempo/Prometheus respectively) is the
/// Collector's job via its own pipeline config, never something a service decides.
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
    /// (ASP.NET Core, HttpClient, EF Core, raw Npgsql, and RabbitMQ tracing; ASP.NET Core,
    /// HttpClient, and runtime metrics; OTLP exporter for every signal, always — the endpoint
    /// defaults to the platform's centralized Collector when unconfigured, and this method throws
    /// if it still resolves blank; Prometheus scrape endpoint by default).
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

        var configuration = builder.Configuration;
        var environmentName = builder.Environment.EnvironmentName;

        var otlpEndpoint = configuration[options.OtlpEndpointConfigurationKey];
        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            otlpEndpoint = options.DefaultOtlpEndpoint;
        }

        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            throw new InvalidOperationException(
                $"OTLP endpoint is not configured for service '{serviceName}'. Set " +
                $"'{options.OtlpEndpointConfigurationKey}' in configuration, or a non-blank " +
                $"{nameof(KartObservabilityOptions)}.{nameof(KartObservabilityOptions.DefaultOtlpEndpoint)}.");
        }

        var otlpProtocol = ResolveOtlpProtocol(configuration, options);
        var samplingRatio = ResolveSamplingRatio(configuration, options);

        var resourceAttributes = new Dictionary<string, object>
        {
            ["service.name"] = serviceName,
            ["service.instance.id"] = options.ServiceInstanceId,
            ["deployment.environment"] = environmentName,
        };
        if (!string.IsNullOrWhiteSpace(options.ServiceVersion))
        {
            resourceAttributes["service.version"] = options.ServiceVersion;
        }

        builder.Host.UseSerilog((context, services, loggerConfiguration) =>
        {
            loggerConfiguration
                // Sensible high-TPS-safe floor: applied before ReadFrom.Configuration below, so
                // any service's own "Serilog" appsettings section still wins when it sets these
                // explicitly. Without this, a service that never configured a "Serilog" section
                // at all defaults to logging every ASP.NET Core framework-internal Information
                // line (request started/finished, routing, etc.) — background noise that drowns
                // out application signal in Loki at real traffic volume.
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
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
                .Enrich.WithProperty("Service", serviceName)
                .Enrich.WithProperty("service.instance.id", options.ServiceInstanceId);

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
            // TraceId with its Tempo span, depends on this sink existing. Batched (not
            // fire-per-line) so a Collector hiccup under sustained high-TPS load queues instead of
            // blocking the request thread that triggered the log call.
            loggerConfiguration.WriteTo.OpenTelemetry(otlpOptions =>
            {
                otlpOptions.Endpoint = otlpEndpoint;
                otlpOptions.Protocol = otlpProtocol switch
                {
                    KartOtlpProtocol.HttpProtobuf => Serilog.Sinks.OpenTelemetry.OtlpProtocol.HttpProtobuf,
                    _ => Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc,
                };
                otlpOptions.ResourceAttributes = resourceAttributes;
                otlpOptions.BatchingOptions.BatchSizeLimit = options.OtlpMaxExportBatchSize;
                otlpOptions.BatchingOptions.QueueLimit = options.OtlpMaxQueueSize;
                otlpOptions.BatchingOptions.BufferingTimeLimit = TimeSpan.FromMilliseconds(options.OtlpScheduledDelayMilliseconds);
            });

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

        var exportProtocol = otlpProtocol switch
        {
            KartOtlpProtocol.HttpProtobuf => OtlpExportProtocol.HttpProtobuf,
            _ => OtlpExportProtocol.Grpc,
        };

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddAttributes(resourceAttributes))
            .WithTracing(tracing =>
            {
                tracing
                    // A sampled parent is always honored regardless of ratio — this only governs
                    // the root-span decision, so a 100%-tier service (the Order Saga) still gets
                    // full coverage simply by setting its ratio to 1.0 (the default).
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
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

                tracing.AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(otlpEndpoint);
                    otlp.Protocol = exportProtocol;
                    otlp.BatchExportProcessorOptions.MaxQueueSize = options.OtlpMaxQueueSize;
                    otlp.BatchExportProcessorOptions.MaxExportBatchSize = options.OtlpMaxExportBatchSize;
                    otlp.BatchExportProcessorOptions.ScheduledDelayMilliseconds = options.OtlpScheduledDelayMilliseconds;
                });
            })
            .WithMetrics(metrics =>
            {
                // RED metrics (rate/errors/duration) on every HTTP endpoint, per
                // observability-standards.md — ASP.NET Core's own instrumentation already
                // emits http.server.request.duration; scraped at /metrics.
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (options.EnablePrometheusScrapeEndpoint)
                {
                    metrics.AddPrometheusExporter();
                }

                metrics.AddOtlpExporter((otlp, readerOptions) =>
                {
                    otlp.Endpoint = new Uri(otlpEndpoint);
                    otlp.Protocol = exportProtocol;
                    readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                        options.OtlpMetricsExportIntervalMilliseconds;
                });
            });

        return builder;
    }

    private static KartOtlpProtocol ResolveOtlpProtocol(
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        KartObservabilityOptions options)
    {
        var configured = configuration[options.OtlpProtocolConfigurationKey];
        return Enum.TryParse<KartOtlpProtocol>(configured, ignoreCase: true, out var parsed)
            ? parsed
            : options.DefaultOtlpProtocol;
    }

    private static double ResolveSamplingRatio(
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        KartObservabilityOptions options)
    {
        var configured = configuration[options.TracingSamplingRatioConfigurationKey];
        return double.TryParse(configured, out var parsed) && parsed is >= 0.0 and <= 1.0
            ? parsed
            : options.DefaultTracingSamplingRatio;
    }
}
