using FluentAssertions;
using Kart.Shared.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Kart.Shared.Configuration.Tests;

public class GlobalConfigExtensionsTests
{
    [Fact]
    public void AddKartGlobalConfig_Throws_WhenPathIsNotSet()
    {
        var builder = WebApplication.CreateBuilder();

        var act = () => builder.AddKartGlobalConfig();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GlobalConfig:Path*appsettings.Local.json*");
    }

    [Fact]
    public void AddKartGlobalConfig_LayersTheGlobalConfigFile_WhenPathIsSet()
    {
        var globalConfigFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(globalConfigFile, """{ "SomeSecret": "from-globalconfig" }""");

            var builder = WebApplication.CreateBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GlobalConfig:Path"] = globalConfigFile,
            });

            builder.AddKartGlobalConfig();

            builder.Configuration["SomeSecret"].Should().Be("from-globalconfig");
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

        var act = () => builder.AddKartGlobalConfig(options =>
        {
            options.PathConfigurationKey = "Custom:Path";
        });

        // The custom key resolves to a non-empty value, so no exception — proves the
        // configured key (not the default) was the one actually read.
        act.Should().Throw<FileNotFoundException>();
    }
}
