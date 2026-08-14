using FluentAssertions;
using Kart.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Kart.Shared.Messaging.Tests;

public class KartMessagingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddKartMessageBusManifest_ResolvesLazilyAndLoadsFromFactoryPath()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{"service":"svc","exchanges":[],"externalExchanges":[],"publishedEvents":[],"queues":[],"deadLetterQueues":[]}""");
        var services = new ServiceCollection();

        services.AddKartMessageBusManifest(_ => path);
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<MessageBusManifest>().Service.Should().Be("svc");
    }

    [Fact]
    public void AddKartRabbitMqConnectionFactory_BuildsFactoryFromSuppliedSettings()
    {
        var services = new ServiceCollection();

        services.AddKartRabbitMqConnectionFactory(_ => new RabbitMqConnectionSettings("broker.local", 5673, "user", "pass"));
        var provider = services.BuildServiceProvider();
        var factory = (ConnectionFactory)provider.GetRequiredService<IConnectionFactory>();

        factory.HostName.Should().Be("broker.local");
        factory.Port.Should().Be(5673);
        factory.UserName.Should().Be("user");
        factory.Password.Should().Be("pass");
    }

    [Fact]
    public void AddKartRabbitMqConnectionFactory_NoUserName_LeavesRabbitClientDefaultCredentials()
    {
        var services = new ServiceCollection();

        services.AddKartRabbitMqConnectionFactory(_ => new RabbitMqConnectionSettings("localhost"));
        var provider = services.BuildServiceProvider();
        var factory = (ConnectionFactory)provider.GetRequiredService<IConnectionFactory>();

        factory.UserName.Should().Be("guest");
    }
}
