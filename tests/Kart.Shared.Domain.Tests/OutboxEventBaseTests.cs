using FluentAssertions;
using Kart.Shared.Domain;
using Xunit;

namespace Kart.Shared.Domain.Tests;

public class OutboxEventBaseTests
{
    private sealed class TestOutboxEvent : OutboxEventBase
    {
        private TestOutboxEvent()
        {
        }

        public TestOutboxEvent(Guid id, Guid aggregateId, string eventType, string payload, DateTimeOffset occurredAt)
            : base(id, aggregateId, eventType, payload, occurredAt)
        {
        }
    }

    private static TestOutboxEvent CreateEvent() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "TestEvent",
        "{}",
        DateTimeOffset.UtcNow);

    [Fact]
    public void NewEvent_IsUnpublished()
    {
        var outboxEvent = CreateEvent();

        outboxEvent.PublishedAt.Should().BeNull();
    }

    [Fact]
    public void MarkPublished_SetsPublishedAt()
    {
        var outboxEvent = CreateEvent();
        var publishedAt = DateTimeOffset.UtcNow;

        outboxEvent.MarkPublished(publishedAt);

        outboxEvent.PublishedAt.Should().Be(publishedAt);
    }

    [Fact]
    public void MarkPublished_CalledTwice_Throws_AndKeepsTheOriginalTimestamp()
    {
        var outboxEvent = CreateEvent();
        var firstPublish = DateTimeOffset.UtcNow;
        outboxEvent.MarkPublished(firstPublish);

        var act = () => outboxEvent.MarkPublished(firstPublish.AddMinutes(5));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{outboxEvent.Id}*already published*");
        outboxEvent.PublishedAt.Should().Be(firstPublish);
    }

    [Fact]
    public void Constructor_SetsAllFields()
    {
        var id = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var outboxEvent = new TestOutboxEvent(id, aggregateId, "TestEvent", "{\"a\":1}", occurredAt);

        outboxEvent.Id.Should().Be(id);
        outboxEvent.AggregateId.Should().Be(aggregateId);
        outboxEvent.EventType.Should().Be("TestEvent");
        outboxEvent.Payload.Should().Be("{\"a\":1}");
        outboxEvent.OccurredAt.Should().Be(occurredAt);
    }
}
