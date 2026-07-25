# Kart.Shared.Domain

Common DDD building blocks shared across Kart's microservices. Generic types only — never a
service's own domain vocabulary.

## What's in this package

- **`Result` / `Result<T>` / `Error`** — the Result/Either pattern `api-standards.md` mandates for
  domain/business errors ("returned via `Result`, not thrown"). `Error` carries the platform-wide
  generic error-code vocabulary (`validation_error`, `not_found`, `conflict`, `unauthorized`) plus
  an `Error.Custom(code, message)` escape hatch for a service's own codes.
- **`AggregateRoot` / `IDomainEvent`** — a base class that collects in-process domain events raised
  during a single unit of work, for infrastructure to translate into Outbox rows within the same
  `SaveChanges` transaction.
- **`OutboxEventBase`** — the generic Transactional Outbox row shape (`Id`/`AggregateId`/
  `EventType`/`Payload`/`OccurredAt`/`PublishedAt`), with the invariant that a row can never be
  marked published twice.

## Adoption status (as of 2026-07-25)

Only **kart-category-service** currently uses the `AggregateRoot`/`IDomainEvent` in-process
domain-event pattern this package generalizes — its `Domain/Common/AggregateRoot.cs` and
`Domain/Common/IDomainEvent.cs` were the source this package was generalized from.

**kart-identity-service does not use this pattern**, and that's an intentional design difference,
not an oversight: its aggregates (`User`, `Session`, `FederatedIdentity`, `ServicePrincipal`, ...)
raise no in-process domain-event list at all. Instead its command handlers construct `OutboxEvent`
rows directly and add them to the `DbContext` alongside the aggregate change in the same
`SaveChangesAsync` call, later relayed by an `OutboxRelayHostedService` poller — an
outbox-row-per-event approach with no `SaveChanges` interceptor and no aggregate-held event list.
`AggregateRoot`'s event-collection convention doesn't map onto that design without rework, so
kart-identity-service is not a current consumer of this type. If/when it (or a future service)
adopts the interceptor-based dispatch model instead, `AggregateRoot`/`IDomainEvent` here is what it
would build on.

`OutboxEventBase` is written to fit both existing shapes (`CategoryOutboxEvent` and
kart-identity-service's own `OutboxEvent`) regardless of which domain-event dispatch mechanism a
service uses — a concrete per-service outbox entity inherits from it either way.

## Usage

```csharp
public sealed class Category : AggregateRoot
{
    public static Result<Category> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Category>(Error.Validation("Name is required."));
        }

        var category = new Category(name);
        category.Raise(new CategoryCreated(category.Id, DateTimeOffset.UtcNow));
        return Result.Success(category);
    }
}
```
