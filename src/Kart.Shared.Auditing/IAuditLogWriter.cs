namespace Kart.Shared.Auditing;

/// <summary>
/// The single write path every service uses to persist an <see cref="AuditLogEntry"/> — "one
/// platform-wide implementation, not built locally by each service" (kart-conventions.md). A
/// service supplies its own concrete writer (e.g. an EF Core-backed one writing to its own
/// `audit_log` table, or one that publishes onto its own outbox) and registers it via
/// <see cref="AuditingExtensions.AddKartAuditing{TWriter}"/>; this package only owns the contract
/// and entry shape, never a service's storage/transport choice.
/// </summary>
public interface IAuditLogWriter
{
    Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}
