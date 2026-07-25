# Kart.Shared.ErrorHandling

Global exception-handling middleware + `ProblemDetails` factory shared across Kart's
microservices, per `kart-conventions.md`'s Error Handling & Response Consistency section: "no
service hand-rolls its own exception middleware or error-response shape."

## What's in this package

- **`KartExceptionHandler`** — an `IExceptionHandler` implementation. Handles FluentValidation's
  `ValidationException` (400, grouped by property), any exception type mapped via
  `KartErrorHandlingOptions.Map<TException>`, and falls back to a generic 500 for anything else —
  never falls through to ASP.NET Core's default handling, so every service's 500 response has the
  same shape. Every branch logs exactly once (`Warning` for a recognized/expected rejection,
  `Error` for a genuine unhandled failure).
- **`KartProblemDetailsFactory`** — builds the RFC 7807 envelope with the platform's two mandatory
  extension fields: `traceId` (current OpenTelemetry trace id, falling back to
  `HttpContext.TraceIdentifier`) and `errorCode` (the stable, machine-readable code every
  `api-contract.yaml`'s `Problem.code` already uses).
- **`KartErrorHandlingOptions`** — the per-service exception → status-code/error-code registry.

## Usage

```csharp
// Program.cs
builder.Services.AddKartErrorHandling(options => options
    .Map<OrderNotFoundException>(StatusCodes.Status404NotFound, "order_not_found")
    .Map<InsufficientStockException>(StatusCodes.Status409Conflict, "insufficient_stock"));

var app = builder.Build();
app.UseKartErrorHandling();
```

No `Handler`/controller needs its own try/catch to translate an exception into a response — that
is this package's job, wired once at startup. Domain/business errors keep using
`Kart.Shared.Domain`'s `Result`/`Error` pattern; exceptions stay reserved for genuine
infrastructure failures, caught exactly once by `KartExceptionHandler`.

## Adoption status (as of 2026-07-25)

Generalizes both reference services' `GlobalExceptionHandler`:

- **kart-category-service** special-cased FluentValidation's `ValidationException` (400) and
  translated everything else to a generic 500 — exactly what you get by registering nothing extra
  via `KartErrorHandlingOptions`.
- **kart-identity-service** special-cased ~15 Application-layer exception types via a hand-written
  `switch`, falling through to ASP.NET Core's default handling for anything unrecognized — the one
  behavior this package deliberately does *not* preserve (an unrecognized exception here is always
  translated to the platform's `ProblemDetails` envelope, never left to fall through), since two
  services must never differ in what shape an unexpected 500 comes back as.

Neither reference service has migrated to this package yet; each would replace its own
`GlobalExceptionHandler` with an `AddKartErrorHandling`/`UseKartErrorHandling` call plus its own
`Map<TException>` registrations for the exception types it used to `switch` on by hand.
