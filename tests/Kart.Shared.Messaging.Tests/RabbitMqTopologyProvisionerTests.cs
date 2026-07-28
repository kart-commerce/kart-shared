using FluentAssertions;
using Kart.Shared.Messaging;
using NSubstitute;
using RabbitMQ.Client;

namespace Kart.Shared.Messaging.Tests;

public class RabbitMqTopologyProvisionerTests
{
    [Fact]
    public void Declare_ExchangesAndExternalExchanges_AreBothDeclared()
    {
        var channel = Substitute.For<IModel>();
        var manifest = new MessageBusManifest(
            "svc",
            Exchanges: [new ExchangeDefinition("own.exchange", "topic", Durable: true)],
            ExternalExchanges: [new ExchangeDefinition("external.exchange", "fanout", Durable: false)],
            PublishedEvents: [],
            Queues: [],
            DeadLetterQueues: []);

        RabbitMqTopologyProvisioner.Declare(channel, manifest);

        channel.Received(1).ExchangeDeclare("own.exchange", "topic", true, false, null);
        channel.Received(1).ExchangeDeclare("external.exchange", "fanout", false, false, null);
    }

    [Fact]
    public void Declare_DeadLetterQueues_AreDeclaredAndBound()
    {
        var channel = Substitute.For<IModel>();
        var manifest = new MessageBusManifest(
            "svc", [], [], [],
            Queues: [],
            DeadLetterQueues: [new DeadLetterQueueDefinition("svc.dlq", "svc.dlx", "svc.dead")]);

        RabbitMqTopologyProvisioner.Declare(channel, manifest);

        channel.Received(1).QueueDeclare("svc.dlq", true, false, false, null);
        channel.Received(1).QueueBind("svc.dlq", "svc.dlx", "svc.dead", null);
    }

    [Fact]
    public void Declare_QueueWithDeadLetter_PassesDeadLetterArgumentsOnDeclare()
    {
        var channel = Substitute.For<IModel>();
        var queue = new QueueDefinition(
            "svc.main.queue",
            Durable: true,
            Bindings: [new QueueBindingDefinition("svc.exchange", "svc.routing.key")],
            DeadLetter: new DeadLetterDefinition("svc.dlx", "svc.dead"),
            RetryLadder: null);
        var manifest = new MessageBusManifest("svc", [], [], [], Queues: [queue], DeadLetterQueues: []);

        RabbitMqTopologyProvisioner.Declare(channel, manifest);

        channel.Received(1).QueueDeclare(
            "svc.main.queue",
            true,
            false,
            false,
            Arg.Is<IDictionary<string, object>>(args =>
                (string)args["x-dead-letter-exchange"] == "svc.dlx" &&
                (string)args["x-dead-letter-routing-key"] == "svc.dead"));
        channel.Received(1).QueueBind("svc.main.queue", "svc.exchange", "svc.routing.key", null);
    }

    [Fact]
    public void Declare_QueueWithoutDeadLetter_PassesNullArgumentsOnDeclare()
    {
        var channel = Substitute.For<IModel>();
        var queue = new QueueDefinition("svc.plain.queue", Durable: false, Bindings: [], DeadLetter: null, RetryLadder: null);
        var manifest = new MessageBusManifest("svc", [], [], [], Queues: [queue], DeadLetterQueues: []);

        RabbitMqTopologyProvisioner.Declare(channel, manifest);

        channel.Received(1).QueueDeclare("svc.plain.queue", false, false, false, null);
    }

    [Fact]
    public void Declare_RetryLadder_DeclaresEachTierWithTtlAndRequeueTarget()
    {
        var channel = Substitute.For<IModel>();
        var retryLadder = new RetryLadderDefinition(
            RequeueTo: "svc.main.queue",
            Tiers: [new RetryTierDefinition("svc.retry.10s", 10_000), new RetryTierDefinition("svc.retry.60s", 60_000)]);
        var queue = new QueueDefinition("svc.main.queue", Durable: true, Bindings: [], DeadLetter: null, RetryLadder: retryLadder);
        var manifest = new MessageBusManifest("svc", [], [], [], Queues: [queue], DeadLetterQueues: []);

        RabbitMqTopologyProvisioner.Declare(channel, manifest);

        channel.Received(1).QueueDeclare(
            "svc.retry.10s", true, false, false,
            Arg.Is<IDictionary<string, object>>(a => (int)a["x-message-ttl"] == 10_000 && (string)a["x-dead-letter-routing-key"] == "svc.main.queue"));
        channel.Received(1).QueueDeclare(
            "svc.retry.60s", true, false, false,
            Arg.Is<IDictionary<string, object>>(a => (int)a["x-message-ttl"] == 60_000 && (string)a["x-dead-letter-routing-key"] == "svc.main.queue"));
    }

    [Fact]
    public void Declare_IsIdempotentToCall_DoesNotThrowWhenCalledTwice()
    {
        var channel = Substitute.For<IModel>();
        var queue = new QueueDefinition("svc.main.queue", Durable: true, Bindings: [], DeadLetter: null, RetryLadder: null);
        var manifest = new MessageBusManifest("svc", [], [], [], Queues: [queue], DeadLetterQueues: []);

        var act = () =>
        {
            RabbitMqTopologyProvisioner.Declare(channel, manifest);
            RabbitMqTopologyProvisioner.Declare(channel, manifest);
        };

        act.Should().NotThrow();
        channel.Received(2).QueueDeclare("svc.main.queue", true, false, false, null);
    }
}
