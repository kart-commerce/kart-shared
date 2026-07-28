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
}
