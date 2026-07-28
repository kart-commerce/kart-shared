using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Formatting.Compact;

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
                .Enrich.WithProperty("service", serviceName);

            var isDevelopment = context.HostingEnvironment.IsDevelopment();

            if (isDevelopment)
            {
                loggerConfiguration.WriteTo.Console(outputTemplate: DevelopmentOutputTemplate);
            }
            else
            {
                loggerConfiguration.WriteTo.Console(new CompactJsonFormatter());
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
                    .AddSource("Npgsql");

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
