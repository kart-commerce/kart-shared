namespace Kart.Shared.Domain;

/// <summary>
/// Marker for a domain event raised in-process by an <see cref="AggregateRoot"/>. Identical
/// across every service that has adopted this convention so far (kart-category-service's
/// Domain/Common/IDomainEvent.cs) — generalized here so it is defined once, not re-typed
/// per service.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
