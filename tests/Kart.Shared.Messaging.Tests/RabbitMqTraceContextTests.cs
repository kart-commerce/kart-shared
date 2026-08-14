using System.Diagnostics;
using FluentAssertions;
using Kart.Shared.Messaging;
using RabbitMQ.Client;

namespace Kart.Shared.Messaging.Tests;

/// <summary>
/// RabbitMQ.Client's own <c>IBasicProperties</c> implementation is internal — only obtainable
/// from a live <c>IModel.CreateBasicProperties()</c> — so <see cref="RabbitMqTraceContext"/>'s
/// header-stamping behavior needs a minimal stand-in. Only <see cref="Headers"/> is exercised by
/// production code; everything else is an unused stub.
/// </summary>
internal sealed class FakeBasicProperties : IBasicProperties
{
    public IDictionary<string, object>? Headers { get; set; }
    public string? AppId { get; set; }
    public string? ClusterId { get; set; }
    public string? ContentEncoding { get; set; }
    public string? ContentType { get; set; }
    public string? CorrelationId { get; set; }
    public byte DeliveryMode { get; set; }
    public string? Expiration { get; set; }
    public string? MessageId { get; set; }
    public bool Persistent { get; set; }
    public byte Priority { get; set; }
    public string? ReplyTo { get; set; }
    public PublicationAddress? ReplyToAddress { get; set; }
    public AmqpTimestamp Timestamp { get; set; }
    public string? Type { get; set; }
    public string? UserId { get; set; }
    public ushort ProtocolClassId => 0;
    public string ProtocolClassName => string.Empty;

    public void ClearAppId() => AppId = null;
    public void ClearClusterId() => ClusterId = null;
    public void ClearContentEncoding() => ContentEncoding = null;
    public void ClearContentType() => ContentType = null;
    public void ClearCorrelationId() => CorrelationId = null;
    public void ClearDeliveryMode() => DeliveryMode = default;
    public void ClearExpiration() => Expiration = null;
    public void ClearHeaders() => Headers = null;
    public void ClearMessageId() => MessageId = null;
    public void ClearPriority() => Priority = default;
    public void ClearReplyTo() => ReplyTo = null;
    public void ClearTimestamp() => Timestamp = default;
    public void ClearType() => Type = null;
    public void ClearUserId() => UserId = null;
    public bool IsAppIdPresent() => AppId is not null;
    public bool IsClusterIdPresent() => ClusterId is not null;
    public bool IsContentEncodingPresent() => ContentEncoding is not null;
    public bool IsContentTypePresent() => ContentType is not null;
    public bool IsCorrelationIdPresent() => CorrelationId is not null;
    public bool IsDeliveryModePresent() => DeliveryMode != default;
    public bool IsExpirationPresent() => Expiration is not null;
    public bool IsHeadersPresent() => Headers is not null;
    public bool IsMessageIdPresent() => MessageId is not null;
    public bool IsPriorityPresent() => Priority != default;
    public bool IsReplyToPresent() => ReplyTo is not null;
    public bool IsTimestampPresent() => Timestamp.UnixTime != 0;
    public bool IsTypePresent() => Type is not null;
    public bool IsUserIdPresent() => UserId is not null;
}

public class RabbitMqTraceContextTests
{
    // A literal, not RabbitMqTraceContext.ActivitySource.Name: reading that static property here
    // would force RabbitMqTraceContext's static constructor to run *during* ActivitySource's own
    // constructor (which synchronously notifies existing listeners' ShouldListenTo) — a re-entrant
    // read of the not-yet-assigned static field, which throws NullReferenceException.
    private const string RabbitMqActivitySourceName = "Kart.Shared.Messaging.RabbitMq";

    private static readonly ActivityListener Listener = new()
    {
        ShouldListenTo = source => source.Name == RabbitMqActivitySourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public RabbitMqTraceContextTests()
    {
        // The SDK only creates an Activity for a source with a listener attached — without this,
        // ActivitySource.StartActivity silently returns null and every assertion below would pass
        // vacuously even if the production code were broken.
        ActivitySource.AddActivityListener(Listener);
    }

    private static IBasicProperties FakeProperties() => new FakeBasicProperties();

    [Fact]
    public void StartPublishActivity_StampsTraceparentAndCorrelationIdHeaders()
    {
        var properties = FakeProperties();

        using var activity = RabbitMqTraceContext.StartPublishActivity("orders.exchange", "order.created", properties);

        activity.Should().NotBeNull();
        properties.Headers.Should().ContainKey(RabbitMqTraceContext.TraceParentHeader);
        properties.Headers.Should().ContainKey(RabbitMqTraceContext.CorrelationIdHeader);
        properties.Headers![RabbitMqTraceContext.CorrelationIdHeader].Should().Be(activity!.TraceId.ToString());
    }

    [Fact]
    public void StartConsumeActivity_ContinuesTheSameTrace_WhenTraceparentHeaderPresent()
    {
        var publishProperties = FakeProperties();
        using var publishActivity = RabbitMqTraceContext.StartPublishActivity("orders.exchange", "order.created", publishProperties);

        using var consumeActivity = RabbitMqTraceContext.StartConsumeActivity("order-created-queue", publishProperties);

        consumeActivity.Should().NotBeNull();
        consumeActivity!.TraceId.Should().Be(publishActivity!.TraceId);
        consumeActivity.ParentSpanId.Should().Be(publishActivity.SpanId);
        consumeActivity.Kind.Should().Be(ActivityKind.Consumer);
    }

    [Fact]
    public void StartConsumeActivity_StartsAFreshRootTrace_WhenHeadersAreMissing()
    {
        var properties = FakeProperties();

        using var activity = RabbitMqTraceContext.StartConsumeActivity("order-created-queue", properties);

        activity.Should().NotBeNull();
        activity!.ParentSpanId.Should().Be(default(ActivitySpanId));
    }

    [Fact]
    public void StartConsumeActivity_StartsAFreshRootTrace_WhenPropertiesAreNull()
    {
        var act = () => RabbitMqTraceContext.StartConsumeActivity("order-created-queue", null);

        act.Should().NotThrow();
    }

    [Fact]
    public void StartPublishActivityFromStoredTraceParent_ContinuesTheStoredTrace_NotActivityCurrent()
    {
        // The original request's trace, captured and finished long before the relay runs.
        var originalProperties = FakeProperties();
        string? storedTraceParent;
        ActivityTraceId originalTraceId;
        using (var originalActivity = RabbitMqTraceContext.StartPublishActivity("orders.exchange", "order.created", originalProperties))
        {
            storedTraceParent = originalProperties.Headers![RabbitMqTraceContext.TraceParentHeader] as string;
            originalTraceId = originalActivity!.TraceId;
        }

        // Simulates an Outbox relay: Activity.Current here belongs to the poller's own unrelated
        // loop iteration, not the original request — the stored traceparent must win regardless.
        using var unrelatedCurrent = new Activity("outbox-poller-iteration").Start();
        var relayProperties = FakeProperties();
        using var relayActivity = RabbitMqTraceContext.StartPublishActivityFromStoredTraceParent(
            "orders.exchange", "order.created", storedTraceParent, relayProperties);

        relayActivity.Should().NotBeNull();
        relayActivity!.TraceId.Should().Be(originalTraceId);
        relayActivity.TraceId.Should().NotBe(unrelatedCurrent.TraceId);
    }

    [Fact]
    public void StartPublishActivityFromStoredTraceParent_FallsBackToActivityCurrent_WhenStoredValueIsUnparseable()
    {
        var properties = FakeProperties();

        using var activity = RabbitMqTraceContext.StartPublishActivityFromStoredTraceParent(
            "orders.exchange", "order.created", storedTraceParent: "not-a-real-traceparent", properties);

        activity.Should().NotBeNull();
        properties.Headers.Should().ContainKey(RabbitMqTraceContext.TraceParentHeader);
    }

    [Fact]
    public void ReadCorrelationId_ReturnsTheStampedValue()
    {
        var properties = FakeProperties();
        using var activity = RabbitMqTraceContext.StartPublishActivity("orders.exchange", "order.created", properties);

        var correlationId = RabbitMqTraceContext.ReadCorrelationId(properties);

        correlationId.Should().Be(activity!.TraceId.ToString());
    }

    [Fact]
    public void ReadCorrelationId_ReturnsNull_WhenHeadersAreMissing()
    {
        var properties = FakeProperties();

        RabbitMqTraceContext.ReadCorrelationId(properties).Should().BeNull();
    }

    [Fact]
    public void HeaderRoundTrip_SurvivesByteArrayEncoding()
    {
        // The RabbitMQ client wire-encodes string header values as byte[] on anything it
        // actually serializes/deserializes over AMQP (a real broker round trip) — simulated here
        // since the fake IBasicProperties used in these tests never touches a real connection.
        var properties = FakeProperties();
        using var publishActivity = RabbitMqTraceContext.StartPublishActivity("orders.exchange", "order.created", properties);
        var traceparent = (string)properties.Headers![RabbitMqTraceContext.TraceParentHeader];
        properties.Headers[RabbitMqTraceContext.TraceParentHeader] = System.Text.Encoding.UTF8.GetBytes(traceparent);

        using var consumeActivity = RabbitMqTraceContext.StartConsumeActivity("order-created-queue", properties);

        consumeActivity!.TraceId.Should().Be(publishActivity!.TraceId);
    }
}
