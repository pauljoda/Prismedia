using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Checks monitor fairness using PostgreSQL's actual nullable timestamp ordering.</summary>
public sealed class EfMonitorStoreOrderingPostgresTests {
    [Fact]
    public async Task NeverSearchedMonitorPrecedesPreviouslySearchedDueMonitor() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var previousId = Guid.NewGuid();
        var neverId = Guid.NewGuid();
        foreach (var id in new[] { previousId, neverId }) {
            db.Acquisitions.Add(new AcquisitionRow {
                Id = id, Status = AcquisitionStatus.WaitingForRelease, Title = "Waiting request",
                ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
            });
        }
        await db.SaveChangesAsync();
        var store = new EfMonitorStore(db);
        var previous = await store.StartAsync(previousId, EntityKind.Book, "Previously checked", null, default);
        var never = await store.StartAsync(neverId, EntityKind.Book, "Never checked", null, default);
        var previousRow = await db.Monitors.FindAsync(previous.Id);
        previousRow!.LastSearchedAt = now.AddDays(-2);
        await db.SaveChangesAsync();

        var due = await store.ListDueMonitorsAsync(360, default);

        Assert.Equal(new[] { never.Id, previous.Id }, due.Select(item => item.MonitorId));
    }
}
