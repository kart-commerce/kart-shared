namespace Kart.Shared.Auditing;

/// <summary>
/// The safe default registered by the parameterless <see cref="AuditingExtensions.AddKartAuditing"/>
/// overload — discards every entry. A service that hasn't wired a real sink yet still gets a
/// working <see cref="IAuditLogWriter"/> to depend on rather than a startup failure; swap in a
/// real writer via the generic overload once the service has somewhere to persist audit rows.
/// </summary>
public sealed class NullAuditLogWriter : IAuditLogWriter
{
    public Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
