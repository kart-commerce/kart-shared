using FluentAssertions;
using Kart.Shared.Messaging;

namespace Kart.Shared.Messaging.Tests;

public class MessageBusManifestTests
{
    private static readonly MessageBusManifest Manifest = new(
        Service: "kart-test-service",
        Exchanges: [new ExchangeDefinition("test.exchange", "topic", Durable: true)],
        ExternalExchanges: [],
        PublishedEvents: [new PublishedEventDefinition("ThingHappened", "test.exchange", "thing.happened")],
        Queues: [new QueueDefinition("test.queue", Durable: true, Bindings: [], DeadLetter: null, RetryLadder: null)],
        DeadLetterQueues: []);

    [Fact]
    public void ExchangeFor_KnownEventType_ReturnsItsExchange()
    {
        Manifest.ExchangeFor("ThingHappened").Should().Be("test.exchange");
    }

    [Fact]
    public void RoutingKeyFor_KnownEventType_ReturnsItsRoutingKey()
    {
        Manifest.RoutingKeyFor("ThingHappened").Should().Be("thing.happened");
    }

    [Fact]
    public void ExchangeFor_UnknownEventType_Throws()
    {
        var act = () => Manifest.ExchangeFor("NoSuchEvent");

        act.Should().Throw<InvalidOperationException>().WithMessage("*NoSuchEvent*");
    }

    [Fact]
    public void EventTypeForRoutingKey_KnownRoutingKey_ReturnsItsEventType()
    {
        Manifest.EventTypeForRoutingKey("thing.happened").Should().Be("ThingHappened");
    }

    [Fact]
    public void EventTypeForRoutingKey_UnknownRoutingKey_Throws()
    {
        var act = () => Manifest.EventTypeForRoutingKey("no.such.key");

        act.Should().Throw<InvalidOperationException>().WithMessage("*no.such.key*");
    }

    [Fact]
    public void GetQueue_KnownName_ReturnsIt()
    {
        Manifest.GetQueue("test.queue").Name.Should().Be("test.queue");
    }

    [Fact]
    public void GetQueue_UnknownName_Throws()
    {
        var act = () => Manifest.GetQueue("no.such.queue");

        act.Should().Throw<InvalidOperationException>().WithMessage("*no.such.queue*");
    }

    [Fact]
    public void TryGetPublishedEvent_KnownEventType_ReturnsTrueAndTheDefinition()
    {
        var found = Manifest.TryGetPublishedEvent("ThingHappened", out var definition);

        found.Should().BeTrue();
        definition.Exchange.Should().Be("test.exchange");
    }

    [Fact]
    public void TryGetPublishedEvent_UnknownEventType_ReturnsFalse()
    {
        var found = Manifest.TryGetPublishedEvent("InternalOnlyMarker", out _);

        found.Should().BeFalse();
    }
}
