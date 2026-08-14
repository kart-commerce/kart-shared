# kart-shared

Versioned event/API contracts + generic cross-cutting .NET libraries for every Kart microservice.
Consumed as **published NuGet packages**, never as shared source — per
[`PLATFORM_BLUEPRINT.md` §2.2](https://github.com/kart-commerce/kart-platform/blob/main/docs/PLATFORM_BLUEPRINT.md#22-validating--improving-your-proposed-list)
item 3: "treat it like a public API with semver". This repo intentionally contains **no domain
logic** and **no service-specific types** — only things generic enough to be identical across all
18 Kart services.

## What's in here

| Directory | Contents |
|---|---|
| [`contracts/`](contracts/) | Synced, read-only copies of every approved service's `api-contract.yaml` + `event-contract.md`. See [`contracts/README.md`](contracts/README.md) — regenerated from `kart-platform`, never hand-edited here. |
| [`src/`](src/) | Six versioned NuGet packages (below). |
| [`tests/`](tests/) | One xUnit test project per `src/` package. |

## Packages

| Package | Purpose | README |
|---|---|---|
| `Kart.Shared.Domain` | `Result`/`Error` (Result pattern for domain errors), `AggregateRoot` + `IDomainEvent` (in-process domain events), `OutboxEventBase` (Transactional Outbox row shape) | [src/Kart.Shared.Domain](src/Kart.Shared.Domain/README.md) |
| `Kart.Shared.ErrorHandling` | Global exception-handling middleware + RFC 7807 `ProblemDetails` factory (`traceId`/`errorCode` extensions) | [src/Kart.Shared.ErrorHandling](src/Kart.Shared.ErrorHandling/README.md) |
| `Kart.Shared.Observability` | Serilog + OpenTelemetry SDK wiring (ASP.NET Core/HttpClient/Npgsql/EF Core/RabbitMQ instrumentation, OTLP exporter for every signal, Prometheus scrape endpoint, configurable sampling/high-TPS batch tuning) behind one DI call | [src/Kart.Shared.Observability](src/Kart.Shared.Observability/README.md) |
| `Kart.Shared.Auditing` | `IAuditLogWriter` contract + `AuditLogEntry` shape for the platform-wide audit trail | [src/Kart.Shared.Auditing](src/Kart.Shared.Auditing/README.md) |
| `Kart.Shared.Configuration` | Per-machine GlobalConfig bootstrap (`appsettings.Local.json` override + `GlobalConfig:Path` external secrets file) behind one DI call | [src/Kart.Shared.Configuration](src/Kart.Shared.Configuration/README.md) |
| `Kart.Shared.Messaging` | RabbitMQ topology-from-manifest wiring (manifest types/loader, idempotent topology declaration, startup hosted service, retry-ladder-aware consumer base class) behind a few DI calls | [src/Kart.Shared.Messaging](src/Kart.Shared.Messaging/README.md) |

Each package follows the same "one DI registration call per service" pattern
(`AddKart*`/`UseKart*`), so a consuming service's `Program.cs` wiring looks the same regardless of
which packages it takes a dependency on.

## Consuming these packages

Reference the package(s) a service needs in its own `.csproj`:

```xml
<PackageReference Include="Kart.Shared.Domain" Version="0.1.0" />
<PackageReference Include="Kart.Shared.ErrorHandling" Version="0.1.0" />
<PackageReference Include="Kart.Shared.Observability" Version="0.4.0" />
<PackageReference Include="Kart.Shared.Auditing" Version="0.1.0" />
<PackageReference Include="Kart.Shared.Configuration" Version="0.1.0" />
<PackageReference Include="Kart.Shared.Messaging" Version="0.3.0" />
```

There is no published feed yet (see `Directory.Build.props`'s note) — until one exists, packages
are built locally via `dotnet pack` (below) and referenced via a local NuGet source or a project
reference during development.

## Building, testing, packing

```bash
dotnet restore Kart.Shared.sln
dotnet build Kart.Shared.sln --configuration Release
dotnet test Kart.Shared.sln --configuration Release
dotnet pack Kart.Shared.sln --configuration Release   # -> artifacts/packages/*.nupkg
dotnet format Kart.Shared.sln --verify-no-changes      # coding-standards gate
```

CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) runs all of the above via
`kart-devops`'s reusable `dotnet-service-ci.yml` workflow, with the Docker smoke-build step
disabled — this repo publishes NuGet packages, it does not ship a container.

## Versioning

Every package starts at `0.1.0` and follows SemVer from here on (`Directory.Build.props`). A
breaking change to any public type bumps the major version, per the same breaking-change
definition `kart-conventions.md`'s API Versioning section applies to HTTP/gRPC contracts.

## Standards this repo follows

Design and engineering standards live in `kart-platform`, not duplicated here:

- [`docs/standards/kart-conventions.md`](https://github.com/kart-commerce/kart-platform/blob/main/docs/standards/kart-conventions.md) — Kart-specific policy (Observability, Error Handling, API Versioning sections apply directly to the packages in this repo).
- `agent-reusables/docs/standards/coding-standards.md`, `observability-standards.md`, `api-standards.md` — the project-agnostic rules the above layers on top of.
- [`docs/releases/generated/release-0-platform-bootstrap.md`](https://github.com/kart-commerce/kart-platform/blob/main/docs/releases/generated/release-0-platform-bootstrap.md) — this repo's entry in the Platform Bootstrap release, including the Quality Gate checklist it must satisfy.

## Top risk (from release-0's Common Mistakes)

> Letting kart-shared become a dumping ground for domain logic instead of only versioned
> contracts + generic libs.

Before adding anything here, check: would this be identical if copy-pasted into every one of the
18 services with only the namespace changed? If the answer involves any service-specific
vocabulary (an entity name, a business rule, a bounded-context concept), it belongs in that
service's own repo, not here.
