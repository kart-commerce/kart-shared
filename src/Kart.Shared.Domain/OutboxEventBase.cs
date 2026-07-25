namespace Kart.Shared.Domain;

/// <summary>
/// Generalizes the Transactional Outbox row shape duplicated near-identically across services
/// (kart-category-service's <c>CategoryOutboxEvent</c>: OutboxId/CategoryId/EventType/Payload/
/// OccurredAt/PublishedAt; kart-identity-service's <c>OutboxEvent</c>: EventId/AggregateId/
/// EventType/Payload/OccurredAt/PublishedAt). A concrete per-service outbox entity inherits from
/// this and adds whatever service-specific audit columns (CreatedBy/UpdatedBy) or aggregate-typed
/// convenience members it needs — this base intentionally never encodes a service's own
/// vocabulary (no "CategoryId" here, only the generic <see cref="AggregateId"/>).
///
/// Invariant this base adds beyond either existing implementation: once <see cref="PublishedAt"/>
/// is set it can never be overwritten — <see cref="MarkPublished"/> throws on a second call. Both
/// services' outbox relays only ever poll for rows where PublishedAt is still null, so neither
/// actually depended on being able to call it twice; this closes that as a latent bug rather than
/// dropping any exercised behavior.
/// </summary>
public abstract class OutboxEventBase
{
    public Guid Id { get; protected set; }

    public Guid AggregateId { get; protected set; }

    public string EventType { get; protected set; } = string.Empty;

    public string Payload { get; protected set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; protected set; }

    public DateTimeOffset? PublishedAt { get; protected set; }

    /// <summary>EF Core materialization constructor.</summary>
    protected OutboxEventBase()
    {
    }

    protected OutboxEventBase(Guid id, Guid aggregateId, string eventType, string payload, DateTimeOffset occurredAt)
    {
        Id = id;
        AggregateId = aggregateId;
        EventType = eventType;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    /// <summary>
    /// Marks this row as relayed. Throws if already published — see the type-level remarks for
    /// why this is a strictly-enforced invariant rather than a plain setter.
    /// </summary>
    public void MarkPublished(DateTimeOffset publishedAt)
    {
        if (PublishedAt is not null)
        {
            throw new InvalidOperationException(
                $"Outbox event {Id} was already published at {PublishedAt:O} and cannot be re-published.");
        }

        PublishedAt = publishedAt;
    }
}
