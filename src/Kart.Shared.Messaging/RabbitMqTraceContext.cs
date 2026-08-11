using System.Diagnostics;
using System.Text;
using RabbitMQ.Client;

namespace Kart.Shared.Messaging;

/// <summary>
/// W3C Trace Context propagation over RabbitMQ message headers. OpenTelemetry's ASP.NET Core/
/// HttpClient auto-instrumentation (wired by <c>Kart.Shared.Observability</c>) has no equivalent
/// for RabbitMQ — that package's own README flags this as "the concrete next gap to close ...
/// once a first service needs it" (kart-devops observability-standards.md, "any Kart service
/// publishing/consuming RabbitMQ messages must ... add manual W3C Trace Context propagation on
/// message headers"). This is that first close: one <see cref="ActivitySource"/> every
/// publisher/consumer in the platform should use, so a RabbitMQ hop shows up in Tempo as a real
/// span — not a silent gap between "outbox row written" and "read model updated" — and a single
/// TraceId keeps working across the broker the same way it already does across an HTTP call.
/// </summary>
public static class RabbitMqTraceContext
{
    /// <summary>Standard W3C header name (https://www.w3.org/TR/trace-context/) — same name HTTP uses, so a trace started at the gateway continues unbroken through a RabbitMQ hop.</summary>
    public const string TraceParentHeader = "traceparent";

    /// <summary>Human-readable duplicate of the trace id, for operators grepping raw broker payloads/headers who don't want to decode a traceparent string by hand.</summary>
    public const string CorrelationIdHeader = "CorrelationId";

    /// <summary>Every service's RabbitMQ publish/consume span comes from this one shared source, so the OTel Collector/Tempo see them without each service registering (and someone forgetting to register) its own ActivitySource name.</summary>
    public static readonly ActivitySource ActivitySource = new("Kart.Shared.Messaging.RabbitMq", "1.0.0");

    /// <summary>
    /// Starts a Producer-kind Activity for a publish to <paramref name="exchange"/>/
    /// <paramref name="routingKey"/> — a child of whatever Activity.Current already is (typically
    /// the inbound HTTP request that triggered this publish, or the outbox relay's own polling
    /// activity) — and stamps its W3C <c>traceparent</c> plus a readable <c>CorrelationId</c> onto
    /// <paramref name="properties"/>'s headers so the consumer on the other end can continue the
    /// exact same trace. Always call this before <c>BasicPublish</c>, and always <c>Dispose()</c>
    /// (or use a <c>using</c>) the returned Activity once the publish call returns.
    /// </summary>
    public static Activity? StartPublishActivity(string exchange, string routingKey, IBasicProperties properties)
    {
        var activity = ActivitySource.StartActivity($"{exchange} publish", ActivityKind.Producer);
        var current = activity ?? Activity.Current;

        if (current is not null)
        {
            properties.Headers ??= new Dictionary<string, object>();
            properties.Headers[TraceParentHeader] = current.Id ?? FormatTraceParent(current.Context);
            properties.Headers[CorrelationIdHeader] = current.TraceId.ToString();
        }

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", exchange);
        activity?.SetTag("messaging.rabbitmq.routing_key", routingKey);
        return activity;
    }

    /// <summary>
    /// Same as <see cref="StartPublishActivity"/>, but for a Transactional Outbox relay: the
    /// publish happens from a background poller loop, on an async context wholly unrelated to
    /// the original request that wrote the outbox row — <c>Activity.Current</c> there is null or
    /// belongs to the poller's own unrelated iteration, never the original request. Pass the
    /// <c>traceparent</c> string persisted on the outbox row at write time (e.g.
    /// <c>ProductOutboxEvent.TraceParent</c>/<c>AdminAction.TraceParent</c>) so this publish span
    /// — and the CorrelationId/traceparent headers it stamps for the eventual consumer — continue
    /// the *original* request's trace, not a disconnected new one. Falls back to
    /// <see cref="StartPublishActivity"/>'s Activity.Current behavior when
    /// <paramref name="storedTraceParent"/> is null/unparseable (e.g. a row written before this
    /// column existed).
    /// </summary>
    public static Activity? StartPublishActivityFromStoredTraceParent(string exchange, string routingKey, string? storedTraceParent, IBasicProperties properties)
    {
        if (storedTraceParent is null || !ActivityContext.TryParse(storedTraceParent, traceState: null, out var parentContext))
        {
            return StartPublishActivity(exchange, routingKey, properties);
        }

        var activity = ActivitySource.StartActivity($"{exchange} publish", ActivityKind.Producer, parentContext);

        properties.Headers ??= new Dictionary<string, object>();
        properties.Headers[TraceParentHeader] = activity?.Id ?? storedTraceParent;
        properties.Headers[CorrelationIdHeader] = parentContext.TraceId.ToString();

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", exchange);
        activity?.SetTag("messaging.rabbitmq.routing_key", routingKey);
        return activity;
    }

    /// <summary>
    /// Extracts the W3C <c>traceparent</c> (if present) from an inbound message's headers and
    /// starts a Consumer-kind Activity continuing that same trace, so everything this consumer
    /// does/logs beneath it — including a downstream projection write or a further outbound call
    /// — links to the original request's TraceId in Tempo/Loki. Falls back to a fresh root trace
    /// (never throws/returns null on a missing header) so a message published before this
    /// propagation existed, or from an external system, doesn't break consumption.
    /// </summary>
    public static Activity? StartConsumeActivity(string queueName, IBasicProperties? properties)
    {
        var parentContext = ExtractContext(properties);
        var activity = parentContext.HasValue
            ? ActivitySource.StartActivity($"{queueName} consume", ActivityKind.Consumer, parentContext.Value)
            : ActivitySource.StartActivity($"{queueName} consume", ActivityKind.Consumer);

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", queueName);
        return activity;
    }

    /// <summary>Reads the CorrelationId (trace id) off an inbound message's headers without starting an Activity — for logging in code paths that run before/without a consume span (e.g. a dead-letter/DLQ handler).</summary>
    public static string? ReadCorrelationId(IBasicProperties? properties)
    {
        if (properties?.Headers is null || !properties.Headers.TryGetValue(CorrelationIdHeader, out var raw))
        {
            return null;
        }

        return HeaderValueToString(raw);
    }

    private static ActivityContext? ExtractContext(IBasicProperties? properties)
    {
        if (properties?.Headers is null || !properties.Headers.TryGetValue(TraceParentHeader, out var raw))
        {
            return null;
        }

        var traceparent = HeaderValueToString(raw);
        return traceparent is not null && ActivityContext.TryParse(traceparent, traceState: null, out var context)
            ? context
            : null;
    }

    private static string? HeaderValueToString(object raw) => raw switch
    {
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        string s => s,
        _ => raw.ToString(),
    };

    private static string FormatTraceParent(ActivityContext context) =>
        $"00-{context.TraceId}-{context.SpanId}-{(context.TraceFlags.HasFlag(ActivityTraceFlags.Recorded) ? "01" : "00")}";
}
