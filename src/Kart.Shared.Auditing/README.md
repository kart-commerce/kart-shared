# Kart.Shared.Auditing

Shared audit-log-entry writer abstraction — "one platform-wide implementation, not built locally
by each service" (`kart-conventions.md`'s Observability section).

## What's in this package

- **`AuditLogEntry`** — a single audit-trail fact: who did what, to which entity, when, from which
  service. Created via `AuditLogEntry.Create(...)`.
- **`IAuditLogWriter`** — the single write path every service uses to persist an entry. This
  package owns only the contract and entry shape, never a service's storage/transport choice.
- **`NullAuditLogWriter`** — the safe default: a service that hasn't wired a real sink yet still
  gets a working `IAuditLogWriter` to depend on rather than a startup failure.

## Usage

```csharp
// Program.cs — safe default, discards every entry until a real sink exists
builder.Services.AddKartAuditing();

// Once the service has somewhere to persist audit rows (its own EF Core table,
// its own outbox, ...), swap in a real writer:
builder.Services.AddKartAuditing<EfCoreAuditLogWriter>();
```

```csharp
await auditLogWriter.WriteAsync(AuditLogEntry.Create(
    serviceName: "kart-category-service",
    actorId: currentUser.Id,
    actorType: "admin",
    action: "category.archived",
    entityType: "Category",
    entityId: category.Id.ToString()));
```

## Provenance note on `AuditLogEntry`'s shape

The brief for this package points at `kart-requirements.md` §24.3 as the source of the shared
audit-logging requirement. As of this writing, `kart-requirements.md` §24 only has §24.1
(Cross-Cutting RBAC Model) and §24.2 (SSO / Identity Federation) — there is no §24.3 yet. This
entry shape is therefore **inferred**, not transcribed, from what's already load-bearing elsewhere
in that BRD: the `AdminActionPerformed` event (§10 Event Catalog: `adminId`, `action`, `entityId`,
"audit trail" consumer note) and the `CreatedBy`/`UpdatedBy` convention already present on every
service's own entities. Revisit `AuditLogEntry`'s fields if/when a real §24.3 is written in
`kart-requirements.md`.

## Adoption status (as of 2026-07-25)

No service has wired a concrete `IAuditLogWriter` yet — this package exists ahead of any consumer,
scaffolded from the citation above. The first service that needs audit logging should implement
its own writer (EF Core-backed, outbox-backed, or otherwise) and register it via the generic
`AddKartAuditing<TWriter>()` overload; that concrete implementation stays in the owning service's
repo, never here.
