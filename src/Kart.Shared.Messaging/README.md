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

## Building, testing, packing

See the repo-root [`README.md`](../../README.md) — this package builds/tests/packs alongside the
other four via the shared `Kart.Shared.sln`.
