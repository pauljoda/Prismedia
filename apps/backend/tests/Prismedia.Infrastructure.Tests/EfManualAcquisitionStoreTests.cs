using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfManualAcquisitionStoreTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prismedia-manual-acquisition-{Guid.NewGuid():N}");

    [Fact]
    public async Task ScannedEntityStaysTransientUntilUploadThenUsesUpgradeChildTicket() {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "Owned Book.epub");
        await File.WriteAllTextAsync(sourcePath, "owned");
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var entityId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKind.Book.ToCode(),
            Title = "Owned Book",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Role = EntityFileRole.Source,
            Path = sourcePath,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = new EfManualAcquisitionStore(db, null!);

        var target = await store.GetSearchTargetAsync(entityId, CancellationToken.None);

        Assert.NotNull(target);
        Assert.Empty(db.Acquisitions);

        var childId = await store.PrepareAsync(entityId, CancellationToken.None);
        Assert.NotNull(childId);
        var child = await db.Acquisitions.SingleAsync(row => row.Id == childId);
        var parent = await db.Acquisitions.SingleAsync(row => row.Id == child.UpgradeOfAcquisitionId);
        Assert.Equal(AcquisitionStatus.Imported, parent.Status);
        Assert.Equal(sourcePath, parent.FinalSourcePath);
        Assert.Equal(AcquisitionStatus.AwaitingSelection, child.Status);

        var completed = new CompletedAcquisitionUpload(
            Guid.NewGuid().ToString("N"),
            Path.Combine(_root, "payload"),
            "replacement.epub");
        Assert.True(await store.CompleteAsync(child.Id, completed, CancellationToken.None));

        var persistedChild = await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == child.Id);
        var selected = JsonSerializer.Deserialize<SelectedRelease>(persistedChild.SelectedReleaseJson!);
        Assert.Equal(AcquisitionStatus.Downloaded, persistedChild.Status);
        Assert.True(selected?.ManualPick);
        Assert.Contains(await db.DownloadTransfers.AsNoTracking().ToArrayAsync(), transfer =>
            transfer.AcquisitionId == child.Id && transfer.ClientItemId == completed.ClientItemId);
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    public void Dispose() {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
