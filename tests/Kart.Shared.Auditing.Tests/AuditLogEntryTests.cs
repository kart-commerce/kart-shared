using FluentAssertions;
using Kart.Shared.Auditing;
using Xunit;

namespace Kart.Shared.Auditing.Tests;

public class AuditLogEntryTests
{
    [Fact]
    public void Create_PopulatesEveryField_AndStampsOccurredAtNow()
    {
        var before = DateTimeOffset.UtcNow;

        var entry = AuditLogEntry.Create(
            serviceName: "kart-category-service",
            actorId: "user-123",
            actorType: "user",
            action: "category.renamed",
            entityType: "category",
            entityId: "cat-456",
            metadata: new Dictionary<string, object?> { ["oldName"] = "Shoes", ["newName"] = "Footwear" });

        var after = DateTimeOffset.UtcNow;

        entry.EntryId.Should().NotBeEmpty();
        entry.ServiceName.Should().Be("kart-category-service");
        entry.ActorId.Should().Be("user-123");
        entry.ActorType.Should().Be("user");
        entry.Action.Should().Be("category.renamed");
        entry.EntityType.Should().Be("category");
        entry.EntityId.Should().Be("cat-456");
        entry.OccurredAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        entry.Metadata.Should().ContainKey("oldName").WhoseValue.Should().Be("Shoes");
    }

    [Fact]
    public void Create_TwoCalls_ProduceDifferentEntryIds()
    {
        var first = AuditLogEntry.Create("svc", "actor", "user", "action", "entity", "id-1");
        var second = AuditLogEntry.Create("svc", "actor", "user", "action", "entity", "id-1");

        first.EntryId.Should().NotBe(second.EntryId);
    }
}
