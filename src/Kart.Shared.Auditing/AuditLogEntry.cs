namespace Kart.Shared.Auditing;

/// <summary>
/// A single audit-trail fact: who did what, to which entity, when, from which service.
///
/// <b>Assumption/gap note:</b> the brief for this package points at kart-requirements.md §24.3
/// as the source of the shared-audit-logging requirement, and kart-conventions.md's Observability
/// section repeats that citation verbatim ("the same … pattern kart-requirements.md §24.3 already
/// establishes for Kart.Shared.Auditing"). As of this writing kart-requirements.md's §24 only has
/// §24.1 (Cross-Cutting RBAC Model) and §24.2 (SSO / Identity Federation) — there is no §24.3.
/// This shape is therefore inferred, not transcribed, from what IS already load-bearing
/// elsewhere in that BRD: the <c>AdminActionPerformed</c> event (§10 Event Catalog: adminId,
/// action, entityId, "audit trail" consumer note), and the CreatedBy/UpdatedBy convention already
/// present on every service's own entities (e.g. CategoryOutboxEvent, OutboxEvent). Revisit this
/// shape if/when a real §24.3 is written.
/// </summary>
public sealed record AuditLogEntry(
    Guid EntryId,
    string ServiceName,
    string ActorId,
    string ActorType,
    string Action,
    string EntityType,
    string EntityId,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, object?>? Metadata = null)
{
    public static AuditLogEntry Create(
        string serviceName,
        string actorId,
        string actorType,
        string action,
        string entityType,
        string entityId,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(
            Guid.NewGuid(),
            serviceName,
            actorId,
            actorType,
            action,
            entityType,
            entityId,
            DateTimeOffset.UtcNow,
            metadata);
}
