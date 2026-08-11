namespace Kart.Shared.Observability;

/// <summary>
/// Ambient "which named business Flow is this log line part of" context — the platform-wide
/// tracing/logging standard's <c>Flow</c> field (e.g. "ProductCatalogManagementAdmin"), layered
/// on top of kart-conventions.md's existing mandatory <c>traceId</c>/<c>service</c>/<c>level</c>
/// fields rather than replacing them. Generalized here (not baked into any one service) so every
/// subsequent flow reuses the identical mechanism — a controller action or a message consumer's
/// entry point pushes the flow name once, and every structured log line emitted underneath it
/// (including several MediatR handler layers down, across an <c>await</c>) carries the same
/// <c>Flow</c> property automatically via <see cref="FlowEnricher"/>, the same way Serilog's own
/// <c>LogContext</c> already flows ambient properties across async boundaries.
/// </summary>
public static class KartFlowContext
{
    private static readonly AsyncLocal<string?> CurrentFlow = new();

    /// <summary>The Flow name pushed by the innermost still-open <see cref="Push"/> scope on this async call chain, or null outside any flow (e.g. a health check).</summary>
    public static string? Current => CurrentFlow.Value;

    /// <summary>
    /// Marks every log line emitted for the duration of the returned scope — typically one HTTP
    /// request or one consumed message — as belonging to <paramref name="flow"/>. Always used in
    /// a <c>using</c> so the previous value (usually null) is restored once the scope ends,
    /// exactly like <c>Serilog.Context.LogContext.PushProperty</c>.
    /// </summary>
    public static IDisposable Push(string flow)
    {
        var previous = CurrentFlow.Value;
        CurrentFlow.Value = flow;
        return new PopScope(previous);
    }

    private sealed class PopScope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentFlow.Value = previous;
        }
    }
}
