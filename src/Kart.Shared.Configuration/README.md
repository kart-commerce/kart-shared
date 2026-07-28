# Kart.Shared.Configuration

Per-machine config bootstrap shared across Kart's microservices, per `PLATFORM_BLUEPRINT.md`'s
Configuration Management section: secrets are never committed, never a literal in source.

## The problem this solves

A service needs one machine-specific value to start locally: where its own **GlobalConfig
file** lives — a per-machine, gitignored JSON file holding real local secrets (connection
strings, signing keys, encryption keys, ...). That path itself varies by machine and OS, so it
can't be safely committed in `appsettings.Development.json` either — a Linux developer's absolute
path is wrong for a Windows or Mac teammate, and committing it once is enough to leak a personal
filesystem layout into every clone of the repo.

`Kart.Shared.Configuration` solves this with one more (gitignored) layer: `appsettings.Local.json`.
Each developer creates their own, holding nothing but their own `GlobalConfig:Path`.

## What this package wires up

One call, `AddKartGlobalConfig()`, on `WebApplicationBuilder`, as early as possible in
`Program.cs`:

1. Layers `appsettings.Local.json` (optional, gitignored) on top of configuration read so far.
2. Reads `GlobalConfig:Path` — throwing an actionable `InvalidOperationException` if it's
   missing, since the app cannot start without its secrets.
3. Layers that GlobalConfig file on top too.

## Usage

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddKartGlobalConfig();      // before anything that reads a GlobalConfig-supplied value
builder.AddKartObservability("kart-category-service");
```

Each developer then creates `appsettings.Local.json` (copied from the service's committed
`appsettings.Local.json.example`) with just:

```json
{
  "GlobalConfig": {
    "Path": "/absolute/path/to/your/kart-internals/<service-name>/globalconfig.json"
  }
}
```

`appsettings.Local.json` and the GlobalConfig file itself must both be gitignored by the
consuming service — this package only wires the loading, not the ignore rules.

## Options

`KartGlobalConfigOptions` lets a service rename the local override file or the config key, but
the defaults (`appsettings.Local.json`, `GlobalConfig:Path`) match every service's convention —
most services need pass nothing at all.
