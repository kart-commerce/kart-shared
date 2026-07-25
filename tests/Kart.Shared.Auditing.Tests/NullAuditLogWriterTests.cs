using FluentAssertions;
using Kart.Shared.Auditing;
using Xunit;

namespace Kart.Shared.Auditing.Tests;

public class NullAuditLogWriterTests
{
    [Fact]
    public async Task WriteAsync_CompletesWithoutThrowing()
    {
        var writer = new NullAuditLogWriter();
        var entry = AuditLogEntry.Create("svc", "actor", "user", "action", "entity", "id-1");

        var act = async () => await writer.WriteAsync(entry);

        await act.Should().NotThrowAsync();
    }
}
