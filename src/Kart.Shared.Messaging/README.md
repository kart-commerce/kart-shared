# Kart.Shared.Messaging

Generic RabbitMQ topology-from-manifest wiring shared by every Kart service, per
`kart-conventions.md`'s Messaging section. This package holds the mechanics; it never holds a
service's own manifest content, connection defaults, or business logic.

## The problem this solves

Every service that speaks to RabbitMQ declares its entire topology from its own
`contracts/message-bus-manifest.json`, then wires up the same handful of mechanical pieces:

- Parse the manifest into a strongly-typed shape.
- Idempotently declare every exchange/queue/binding/dead-letter/retry-tier it describes against a
  live channel.
- Declare that topology once at startup (best-effort — a broker outage at boot must not crash the
  process).
- For consumers: reconnect on drop, manual ack/nack, route exhausted retries to the queue's own
  DLX via a custom retry-count header.

That mechanism was identical, copy-pasted source, across every service. This package is the single
copy.

## What stays in each service

- Its own `contracts/message-bus-manifest.json` — the actual topology data.
- Its own `RabbitMqOptions` (or equivalent) — connection host/port/credentials defaults and
  validation are a per-service call (e.g. whether credentials are required at all).
- Its own publishers and consumers' business logic (deserializing a specific event type, calling
  into its own application layer).

## Usage

```csharp
services.AddKartMessageBusManifest(sp => sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value.ManifestPath);
services.AddKartRabbitMqConnectionFactory(sp =>
{
    var options = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
    return new RabbitMqConnectionSettings(options.HostName, options.Port, options.UserName, options.Password);
});
services.AddKartRabbitMqTopologyStartup();
```

A consumer derives `RabbitMqConsumerHostedServiceBase`, supplying its own queue name, payload
dispatch, and a service-namespaced retry-count header (e.g. `x-{service}-retry-count`) so retried
messages from different services never collide:

```csharp
public sealed class OrderPlacedConsumerHostedService(
    IConnectionFactory connectionFactory,
    MessageBusManifest manifest,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderPlacedConsumerHostedService> logger)
    : RabbitMqConsumerHostedServiceBase(connectionFactory, manifest, scopeFactory, logger, "x-order-service-retry-count")
{
    protected override string QueueName => "order.order-placed.queue";

    protected override async Task ProcessAsync(ReadOnlyMemory<byte> body, IServiceProvider scopedProvider, CancellationToken cancellationToken)
    {
        // deserialize `body` and dispatch into this service's own application layer
    }
}
```

## Distributed tracing across the broker

OpenTelemetry's auto-instrumentation (wired by `Kart.Shared.Observability`) has no equivalent for
RabbitMQ — a message hop is otherwise a silent gap in Tempo between "outbox row written" and "read
model updated." `RabbitMqTraceContext` closes that gap with one shared `ActivitySource`
(`"Kart.Shared.Messaging.RabbitMq"`, already opted into the tracer by `AddKartObservability`) every
publisher/consumer on the platform uses:

- **Consuming**: `RabbitMqConsumerHostedServiceBase` starts a consume span automatically around
  every delivery — nothing to opt into. Override the 4-arg `ProcessAsync` overload (body +
  `IBasicProperties` + provider + cancellation token) instead of the 3-arg one when a consumer
  needs the inbound headers itself (e.g. to read `RabbitMqTraceContext.ReadCorrelationId` for a
  dead-letter handler); the 3-arg overload keeps working unchanged for every existing consumer.
- **Publishing**: call `RabbitMqTraceContext.StartPublishActivity(exchange, routingKey,
  properties)` immediately before `BasicPublish`, always in a `using` — this stamps a W3C
  `traceparent` (plus a human-readable `CorrelationId`) onto the message's headers so the consumer
  on the other end continues the exact same trace. Publishing from a **Transactional Outbox
  relay** (a background poller with no ambient `Activity.Current` tied to the original request)
  needs `StartPublishActivityFromStoredTraceParent` instead, passing the `traceparent` string
  persisted on the outbox row at write time.
- **Retry redelivery**: `RabbitMqConsumerHostedServiceBase`'s retry-ladder routing carries the
  original message's headers forward (`traceparent`/`CorrelationId` included) onto every retry
  republish — a redelivered message keeps tracing back to its original trace, not a disconnected
  new one.

## Building, testing, packing

See the repo-root [`README.md`](../../README.md) — this package builds/tests/packs alongside the
other four via the shared `Kart.Shared.sln`.
