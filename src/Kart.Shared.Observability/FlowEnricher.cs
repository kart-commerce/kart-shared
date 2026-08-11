using Serilog.Core;
using Serilog.Events;

namespace Kart.Shared.Observability;

/// <summary>Reads <see cref="KartFlowContext.Current"/> and, when set, adds it to the log event as a "Flow" property — wired into every service's pipeline by <see cref="ObservabilityExtensions.AddKartObservability"/> so no service registers this by hand.</summary>
internal sealed class FlowEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var flow = KartFlowContext.Current;
        if (string.IsNullOrEmpty(flow))
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Flow", flow));
    }
}
