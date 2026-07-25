namespace Kart.Shared.Domain;

/// <summary>
/// Base for an aggregate root that collects in-process domain events raised during a single
/// unit of work. Infrastructure translates these into Outbox rows within the same SaveChanges
/// transaction — never dispatched via an in-memory bus directly. Generalized verbatim from
/// kart-category-service's Domain/Common/AggregateRoot.cs (the only service that has adopted
/// this convention so far); kart-identity-service does not yet use it (see this package's
/// README for that gap).
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public Guid Id { get; protected init; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
