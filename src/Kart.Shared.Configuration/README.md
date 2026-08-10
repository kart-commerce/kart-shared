# Kart.Shared.Configuration

Per-machine config bootstrap shared across Kart's microservices, per `PLATFORM_BLUEPRINT.md`'s
Configuration Management section: secrets are never committed, never a literal in source.

## The problem this solves

A service needs one machine-specific value to start locally: where the shared **GlobalConfig
file** lives — a per-machine, gitignored JSON file holding every service's real local secrets
(connection strings, signing keys, encryption keys, ...) plus the platform-wide defaults every
service inherits (RabbitMQ broker location, Redis/Mongo endpoints, the log directory root). That
path itself varies by machine and OS, so it can't be safely committed in
`appsettings.Development.json` either — a Linux developer's absolute path is wrong for a Windows
or Mac teammate, and committing it once is enough to leak a personal filesystem layout into every
clone of the repo.

`Kart.Shared.Configuration` solves this with one more (gitignored) layer: `appsettings.Local.json`.
Each developer creates their own, holding nothing but their own `GlobalConfig:Path` — which every
service on the platform points at the **same** shared file (`kart-internals/globalconfig.json`
locally, one generated file mounted into every container in Docker), not one file per service.

## The file's shape

```json
{
  "Global": {
    "RabbitMq": { "HostName": "localhost", "Port": 5673, "UserName": "kart", "Password": "kart123" },
    "Redis": { "ConnectionString": "localhost:6380" },
    "LogRoot": "/absolute/path/to/kart-internals/logs"
  },
  "Services": {
    "kart-product-service": {
      "ConnectionStrings": { "ProductDatabase": "..." },
      "Mongo": { "Database": "kart_product" }
    },
    "kart-identity-service": {
      "Jwt": { "SigningKey": { "Kid": "...", "PrivateKeyPem": "..." } }
    }
  }
}
```

- **`Global`** — platform-wide defaults every service inherits, so a cross-cutting value (the
  RabbitMQ broker's host/port, a shared Redis endpoint) changes once, not in every service's own
  block.
- **`Services:<serviceName>`** — one block per service, holding only what's actually specific to
  it (connection strings, DB names, JWT keys, per-service RabbitMQ credentials, downstream base
  URLs). A service's block only needs to set the individual leaf keys it wants to override —
  e.g. overriding just `RabbitMq:UserName` still inherits `RabbitMq:HostName`/`Port` from
  `Global` (leaf-key overlay, not whole-section replace).

## What this package wires up

One call, `AddKartGlobalConfig(serviceName)`, on `WebApplicationBuilder`, as early as possible in
`Program.cs`:

1. Layers `appsettings.Local.json` (optional, gitignored) on top of configuration read so far.
2. Reads `GlobalConfig:Path` — throwing an actionable `InvalidOperationException` if it's
   missing, since the app cannot start without its secrets.
3. Layers that GlobalConfig file's `Global` section, then its `Services:<serviceName>` section on
   top, leaf-key by leaf-key (a service's key wins on conflict, everything else it doesn't set is
   inherited from `Global`).
4. Computes `Observability:LogFile:Directory` as `{Global:LogRoot}/{serviceName}`, unless the
   service's own `Services:<serviceName>` block explicitly sets that key itself.

## Usage

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddKartGlobalConfig("kart-category-service");   // before anything reading a GlobalConfig-supplied value
builder.AddKartObservability("kart-category-service");
```

`serviceName` is required — it's what selects which `Services:<serviceName>` block applies, and
there's no reliable way to infer it automatically (assembly names don't consistently match repo
names). Pass the same literal already passed to `AddKartObservability` right after.

Each developer then creates `appsettings.Local.json` (copied from the service's committed
`appsettings.Local.json.example`) with just:

```json
{
  "GlobalConfig": {
    "Path": "/absolute/path/to/your/kart-internals/globalconfig.json"
  }
}
```

Every service's `appsettings.Local.json.example` points at the same shared file — there is only
one `globalconfig.json` for the whole local-dev platform, not one per service.

`appsettings.Local.json` and the GlobalConfig file itself must both be gitignored by the
consuming service — this package only wires the loading, not the ignore rules.

## Options

`KartGlobalConfigOptions` lets a service rename the local override file or the config key, but
the defaults (`appsettings.Local.json`, `GlobalConfig:Path`) match every service's convention —
most services need pass nothing but `serviceName` itself.
