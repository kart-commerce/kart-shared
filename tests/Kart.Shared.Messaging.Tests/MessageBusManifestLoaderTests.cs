using FluentAssertions;
using Kart.Shared.Messaging;

namespace Kart.Shared.Messaging.Tests;

public class MessageBusManifestLoaderTests
{
    private const string ValidManifestJson = """
        {
          "service": "kart-test-service",
          "exchanges": [{ "name": "test.exchange", "type": "topic", "durable": true }],
          "externalExchanges": [],
          "publishedEvents": [{ "eventType": "ThingHappened", "exchange": "test.exchange", "routingKey": "thing.happened" }],
          "queues": [],
          "deadLetterQueues": []
        }
        """;

    [Fact]
    public void Load_ValidManifest_ReturnsDeserializedManifest()
    {
        var path = WriteTempFile(ValidManifestJson);

        var manifest = MessageBusManifestLoader.Load(path);

        manifest.Service.Should().Be("kart-test-service");
        manifest.Exchanges.Should().ContainSingle(e => e.Name == "test.exchange");
        manifest.ExchangeFor("ThingHappened").Should().Be("test.exchange");
    }

    [Fact]
    public void Load_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

        var act = () => MessageBusManifestLoader.Load(path);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Load_JsonNullLiteral_ThrowsInvalidOperationException()
    {
        var path = WriteTempFile("null");

        var act = () => MessageBusManifestLoader.Load(path);

        act.Should().Throw<InvalidOperationException>();
    }

    private static string WriteTempFile(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, contents);
        return path;
    }
}
