using FluentAssertions;
using Kart.Shared.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
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
}
