using System.Reflection;
using FluentAssertions;
using Kart.Shared.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Xunit;

namespace Kart.Shared.Observability.Tests;

public class ObservabilityExtensionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddKartObservability_Throws_WhenServiceNameIsNullOrWhitespace(string? serviceName)
    {
        var builder = WebApplication.CreateBuilder();

        var act = () => builder.AddKartObservability(serviceName!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddKartObservability_RegistersResolvableTracerAndMeterProviders()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddKartObservability("kart-test-service");
        var app = builder.Build();

        app.Services.GetRequiredService<TracerProvider>().Should().NotBeNull();
        app.Services.GetRequiredService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddKartObservability_InvokesTheConfigureCallback()
    {
        var builder = WebApplication.CreateBuilder();
        var configureWasCalled = false;

        builder.AddKartObservability("kart-test-service", options =>
        {
            configureWasCalled = true;
            options.OtlpEndpointConfigurationKey.Should().Be("Observability:Otlp:Endpoint");
        });

        configureWasCalled.Should().BeTrue();
    }

    [Fact]
    public void AddKartObservability_WritesToConfiguredLogFileDirectory()
    {
        var tempDirectory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
            });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:LogFile:Directory"] = tempDirectory,
            });

            builder.AddKartObservability("kart-test-service");
            var app = builder.Build();

            app.Services.GetRequiredService<ILogger<ObservabilityExtensionsTests>>()
                .LogInformation("test message for the file sink");
            Log.CloseAndFlush();

            Directory.GetFiles(tempDirectory, "kart-test-service-*.log").Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void AddKartObservability_AddsNoFileSink_WhenLogFileDirectoryNotConfigured()
    {
        // No assertion beyond "doesn't throw" — Serilog's sink list isn't publicly inspectable,
        // so this just guards the common case (no config key set) builds cleanly with no file I/O.
        var builder = WebApplication.CreateBuilder();

        var act = () => builder.AddKartObservability("kart-test-service");

        act.Should().NotThrow();
    }

    [Fact]
    public void AddKartObservability_BuildsCleanly_WithOtlpEndpointAndEveryOptionConfigured()
    {
        // The OTLP-exporter branches (traces/metrics/logs) are otherwise never exercised by any
        // test in this file — an unreachable endpoint is fine here since OTel exporters connect
        // lazily on first export, never at provider-build time.
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Observability:Otlp:Endpoint"] = "http://localhost:4317",
            ["Observability:Otlp:Protocol"] = "HttpProtobuf",
            ["Observability:Tracing:SamplingRatio"] = "0.25",
        });

        var act = () => builder.AddKartObservability("kart-test-service", options =>
        {
            options.ServiceVersion = "1.2.3";
            options.EnablePrometheusScrapeEndpoint = false;
        });

        act.Should().NotThrow();
        var app = builder.Build();
        app.Services.GetRequiredService<TracerProvider>().Should().NotBeNull();
        app.Services.GetRequiredService<MeterProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddKartObservability_DefaultsServiceInstanceIdToHostNameOrMachineName()
    {
        var builder = WebApplication.CreateBuilder();
        KartObservabilityOptions? captured = null;

        builder.AddKartObservability("kart-test-service", options => captured = options);

        var expected = Environment.GetEnvironmentVariable("HOSTNAME") is { Length: > 0 } hostName
            ? hostName
            : Environment.MachineName;
        captured!.ServiceInstanceId.Should().Be(expected);
    }

    [Theory]
    [InlineData("HttpProtobuf", KartOtlpProtocol.HttpProtobuf)]
    [InlineData("httpprotobuf", KartOtlpProtocol.HttpProtobuf)]
    [InlineData("Grpc", KartOtlpProtocol.Grpc)]
    [InlineData(null, KartOtlpProtocol.Grpc)]
    [InlineData("not-a-real-protocol", KartOtlpProtocol.Grpc)]
    public void ResolveOtlpProtocol_FallsBackToTheConfiguredDefault_WhenConfigKeyIsAbsentOrInvalid(
        string? configuredValue, KartOtlpProtocol expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Otlp:Protocol"] = configuredValue,
            })
            .Build();
        var options = new KartObservabilityOptions { DefaultOtlpProtocol = KartOtlpProtocol.Grpc };

        var resolved = InvokeResolveOtlpProtocol(configuration, options);

        resolved.Should().Be(expected);
    }

    [Theory]
    [InlineData("0.25", 0.25)]
    [InlineData("0", 0.0)]
    [InlineData("1", 1.0)]
    [InlineData(null, 0.5)] // absent -> falls back to DefaultTracingSamplingRatio
    [InlineData("1.5", 0.5)] // out of the valid [0,1] range -> falls back
    [InlineData("-0.1", 0.5)] // out of the valid [0,1] range -> falls back
    [InlineData("not-a-number", 0.5)] // unparseable -> falls back
    public void ResolveSamplingRatio_FallsBackToTheConfiguredDefault_WhenConfigKeyIsAbsentOrInvalid(
        string? configuredValue, double expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Tracing:SamplingRatio"] = configuredValue,
            })
            .Build();
        var options = new KartObservabilityOptions { DefaultTracingSamplingRatio = 0.5 };

        var resolved = InvokeResolveSamplingRatio(configuration, options);

        resolved.Should().Be(expected);
    }

    // ResolveOtlpProtocol/ResolveSamplingRatio are private — the config-parse-with-fallback logic
    // is exactly the part worth unit-testing directly rather than only indirectly through a full
    // AddKartObservability build, where an invalid config value would otherwise fail silently.
    private static KartOtlpProtocol InvokeResolveOtlpProtocol(IConfiguration configuration, KartObservabilityOptions options) =>
        (KartOtlpProtocol)typeof(ObservabilityExtensions)
            .GetMethod("ResolveOtlpProtocol", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [configuration, options])!;

    private static double InvokeResolveSamplingRatio(IConfiguration configuration, KartObservabilityOptions options) =>
        (double)typeof(ObservabilityExtensions)
            .GetMethod("ResolveSamplingRatio", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [configuration, options])!;
}
