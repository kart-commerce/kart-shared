using FluentAssertions;
using Kart.Shared.Domain;
using Xunit;

namespace Kart.Shared.Domain.Tests;

public class AggregateRootTests
{
    private sealed record TestDomainEvent(DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class TestAggregate : AggregateRoot
    {
        public TestAggregate()
        {
            Id = Guid.NewGuid();
        }

        public void DoSomething(DateTimeOffset occurredAt) => Raise(new TestDomainEvent(occurredAt));
    }

    [Fact]
    public void NewAggregate_HasNoDomainEvents()
    {
        var aggregate = new TestAggregate();

        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Raise_AddsTheEvent_ToDomainEvents()
    {
        var aggregate = new TestAggregate();
        var now = DateTimeOffset.UtcNow;

        aggregate.DoSomething(now);

        aggregate.DomainEvents.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new TestDomainEvent(now));
    }

    [Fact]
    public void ClearDomainEvents_EmptiesTheCollection()
    {
        var aggregate = new TestAggregate();
        aggregate.DoSomething(DateTimeOffset.UtcNow);

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.Should().BeEmpty();
    }
}
