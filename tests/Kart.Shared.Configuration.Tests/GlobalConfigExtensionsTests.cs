using FluentAssertions;
using Kart.Shared.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Kart.Shared.Configuration.Tests;

public class GlobalConfigExtensionsTests
{
    [Fact]
    public void AddKartGlobalConfig_Throws_WhenServiceNameIsEmpty()
    {
        var builder = WebApplication.CreateBuilder();

        var act = () => builder.AddKartGlobalConfig(string.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("serviceName");
    }

    [Fact]
    public void AddKartGlobalConfig_Throws_WhenPathIsNotSet()
    {
        var builder = WebApplication.CreateBuilder();

        var act = () => builder.AddKartGlobalConfig("kart-product-service");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GlobalConfig:Path*appsettings.Local.json*");
    }

    [Fact]
    public void AddKartGlobalConfig_LayersGlobalThenService_WithServiceLeafKeysWinningOverGlobal()
    {
        var globalConfigFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(globalConfigFile, """
                {
                  "Global": {
                    "RabbitMq": { "HostName": "localhost", "Port": 5673, "UserName": "kart" },
                    "SharedOnly": "from-global"
                  },
                  "Services": {
                    "kart-product-service": {
                      "RabbitMq": { "UserName": "product-only-user" },
                      "ServiceOnly": "from-service"
                    }
                  }
                }
                """);

            var builder = WebApplication.CreateBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GlobalConfig:Path"] = globalConfigFile,
            });

            builder.AddKartGlobalConfig("kart-product-service");

            // Service overrides the leaf key it sets...
            builder.Configuration["RabbitMq:UserName"].Should().Be("product-only-user");
            // ...but still inherits sibling leaf keys it never touched, from Global.
            builder.Configuration["RabbitMq:HostName"].Should().Be("localhost");
            builder.Configuration["RabbitMq:Port"].Should().Be("5673");
            builder.Configuration["SharedOnly"].Should().Be("from-global");
            builder.Configuration["ServiceOnly"].Should().Be("from-service");
        }
        finally
        {
            File.Delete(globalConfigFile);
        }
    }

    [Fact]
    public void AddKartGlobalConfig_ComputesLogDirectory_FromGlobalLogRootAndServiceName()
    {
        var globalConfigFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(globalConfigFile, """
                { "Global": { "LogRoot": "/var/log/kart" }, "Services": { "kart-product-service": {} } }
                """);

            var builder = WebApplication.CreateBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GlobalConfig:Path"] = globalConfigFile,
            });

            builder.AddKartGlobalConfig("kart-product-service");

            builder.Configuration["Observability:LogFile:Directory"].Should().Be("/var/log/kart/kart-product-service");
        }
        finally
        {
            File.Delete(globalConfigFile);
        }
    }

    [Fact]
    public void AddKartGlobalConfig_DoesNotOverrideExplicitLogDirectory_SetByTheServiceItself()
    {
        var globalConfigFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(globalConfigFile, """
                {
                  "Global": { "LogRoot": "/var/log/kart" },
                  "Services": {
                    "kart-product-service": { "Observability": { "LogFile": { "Directory": "/custom/path" } } }
                  }
                }
                """);

            var builder = WebApplication.CreateBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GlobalConfig:Path"] = globalConfigFile,
            });

            builder.AddKartGlobalConfig("kart-product-service");

            builder.Configuration["Observability:LogFile:Directory"].Should().Be("/custom/path");
        }
        finally
        {
            File.Delete(globalConfigFile);
        }
    }

    [Fact]
    public void AddKartGlobalConfig_UsesCustomOptions_WhenConfigured()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Custom:Path"] = "does-not-matter-for-this-assertion",
        });

        var act = () => builder.AddKartGlobalConfig("kart-product-service", options =>
        {
            options.PathConfigurationKey = "Custom:Path";
        });

        // The custom key resolves to a non-empty value, so no exception — proves the
        // configured key (not the default) was the one actually read.
        act.Should().Throw<FileNotFoundException>();
    }
}
