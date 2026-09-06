using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class TvOwnedEpisodeCoverageRepairTests : IDisposable {
    private readonly string root = Directory.CreateTempSubdirectory("prismedia-owned-coverage-").FullName;
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task ImportedPairRestoresOnlyMissingLinkAndPreservesExistingSourceAndFile() {
        await using var db = CreateContext();
        var fixture = await SeedAsync(db);
        var queued = new List<Guid>();
        var service = Service(db);

        Assert.Equal(1, await service.RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
            (id, _) => { queued.Add(id); return Task.CompletedTask; }, default));
        Assert.Equal(0, await service.RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
            (id, _) => { queued.Add(id); return Task.CompletedTask; }, default));

        Assert.Equal([fixture.Missing.Id], queued);
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Missing.Id)).IsWanted);
        var sources = await db.EntityFiles.AsNoTracking().Where(row => row.Role == EntityFileRole.Source).ToArrayAsync();
        Assert.Equal(2, sources.Length);
        Assert.Contains(sources, source => source.Id == fixture.Source.Id && source.EntityId == fixture.Owner.Id);
        Assert.All(sources, source => Assert.Equal(fixture.Source.Path, source.Path));
        Assert.Equal("unchanged paired video", await File.ReadAllTextAsync(fixture.Source.Path));
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("manual")]
    [InlineData("changed-size")]
    [InlineData("changed-time")]
    [InlineData("missing-file")]
    [InlineData("other-owner")]
    [InlineData("owned-target")]
    [InlineData("replacement")]
    [InlineData("newer-manual-receipt")]
    [InlineData("duplicate-position")]
    public async Task ChangedOrAmbiguousEvidenceDoesNotAlterCoverage(string change) {
        await using var db = CreateContext();
        var fixture = await SeedAsync(db);
        switch (change) {
            case "paused": fixture.Monitor.Status = MonitorStatus.Paused; break;
            case "manual": fixture.Receipt.ImportManualReview = true; break;
            case "changed-size": await File.AppendAllTextAsync(fixture.Source.Path, " changed"); break;
            case "changed-time": File.SetLastWriteTimeUtc(fixture.Source.Path, DateTime.UtcNow.AddHours(1)); break;
            case "missing-file": File.Delete(fixture.Source.Path); break;
            case "other-owner":
                var unrelated = Episode(fixture.Season.Id, 3, "Ocean Adventure", false);
                db.Entities.Add(unrelated);
                db.EntityFiles.Add(Source(unrelated.Id, fixture.Source.Path, fixture.Source.CreatedAt));
                break;
            case "owned-target": db.EntityFiles.Add(Source(fixture.Missing.Id, Path.Combine(root, "other.mkv"), fixture.Source.CreatedAt)); break;
            case "replacement":
                db.Acquisitions.Add(new AcquisitionRow { Id = Guid.NewGuid(), EntityId = fixture.Owner.Id,
                    Kind = EntityKind.VideoEpisode, Status = AcquisitionStatus.Downloading, UpdatedAt = DateTimeOffset.UtcNow });
                break;
            case "newer-manual-receipt":
                db.Acquisitions.Add(new AcquisitionRow { Id = Guid.NewGuid(), EntityId = fixture.Season.Id,
                    Kind = EntityKind.VideoSeason, Status = AcquisitionStatus.Imported, ImportManualReview = true,
                    FinalSourcePath = fixture.Receipt.FinalSourcePath, ImportResultJson = fixture.Receipt.ImportResultJson,
                    UpdatedAt = fixture.Receipt.UpdatedAt.AddSeconds(1) });
                break;
            case "duplicate-position": db.Entities.Add(Episode(fixture.Season.Id, 2, "Mountain Journey", true)); break;
        }
        await db.SaveChangesAsync();

        Assert.Equal(0, await Service(db).RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
            (_, _) => throw new InvalidOperationException("Rejected evidence must not enqueue"), default));
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Missing.Id)).IsWanted);
    }

    [Fact]
    public async Task MonitorPausedAfterPlanningWinsOverTrackedActiveState() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var lease = new BeforeLease(new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)), async () => {
            await using var concurrent = database.CreateContext();
            await concurrent.Monitors.Where(row => row.Id == fixture.Monitor.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MonitorStatus.Paused));
        });

        Assert.Equal(0, await Service(db, lease).RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
            (_, _) => throw new InvalidOperationException("Paused monitor must not enqueue"), default));
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Missing.Id)).IsWanted);
    }

    [Fact]
    public async Task QueueFailureRollsBackBindingsAndFreshRetryRestoresCoverage() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Fixture fixture;
        await using (var setup = database.CreateContext()) fixture = await SeedAsync(setup);
        await using (var db = database.CreateContext()) {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).RepairAsync(
                fixture.Monitor.Id, fixture.Season.Id, async (_, token) => {
                    Assert.NotNull(db.Database.CurrentTransaction);
                    Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Missing.Id, token)).IsWanted);
                    throw new InvalidOperationException("queue unavailable");
                }, default));
        }
        await using (var db = database.CreateContext()) {
            Assert.True((await db.Entities.SingleAsync(row => row.Id == fixture.Missing.Id)).IsWanted);
            Assert.Single(await db.EntityFiles.ToArrayAsync());
            Assert.Equal(1, await Service(db).RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
                (_, _) => Task.CompletedTask, default));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MetadataOrOwnershipChangedWhileWaitingInvalidatesThePlannedEpisode(bool metadataChanges) {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var fixture = await SeedAsync(db);
        var lease = new BeforeLease(new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)), async () => {
            await using var concurrent = database.CreateContext();
            if (metadataChanges) {
                await concurrent.Entities.Where(row => row.Id == fixture.Missing.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Title, "Ocean Adventure"));
            } else {
                concurrent.EntityFiles.Add(Source(fixture.Missing.Id, Path.Combine(root, "other.mkv"), DateTimeOffset.UtcNow));
                await concurrent.SaveChangesAsync();
            }
        });

        Assert.Equal(0, await Service(db, lease).RepairAsync(fixture.Monitor.Id, fixture.Season.Id,
            (_, _) => throw new InvalidOperationException("Changed mapping must not enqueue"), default));
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == fixture.Missing.Id)).IsWanted);
    }

    private EfTvOwnedEpisodeCoverageRepair Service(PrismediaDbContext db, IEntityLifecycleMutationLease? lease = null) =>
        new(db, new EfImportTargetIndex(db), lease ?? new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db)));

    private async Task<Fixture> SeedAsync(PrismediaDbContext db) {
        var now = DateTimeOffset.UtcNow;
        var series = new EntityRow { Id = Guid.NewGuid(), KindCode = EntityKind.VideoSeries.ToCode(), Title = "Show", CreatedAt = now, UpdatedAt = now };
        var season = new EntityRow { Id = Guid.NewGuid(), ParentEntityId = series.Id, KindCode = EntityKind.VideoSeason.ToCode(),
            Title = "Season 1", SortOrder = 1, CreatedAt = now, UpdatedAt = now };
        var owner = Episode(season.Id, 1, "Hidden Garden", false);
        var missing = Episode(season.Id, 2, "Mountain Journey", true);
        var rootId = Guid.NewGuid();
        db.LibraryRoots.Add(new LibraryRootRow { Id = rootId, Path = root, Label = "Test", CreatedAt = now, UpdatedAt = now });
        db.Entities.Add(series);
        await db.SaveChangesAsync();
        db.Entities.Add(season);
        await db.SaveChangesAsync();
        db.Entities.AddRange(owner, missing);
        var folder = Directory.CreateDirectory(Path.Combine(root, "Show", "Season 01")).FullName;
        var source = Source(owner.Id, Path.Combine(folder, "Show - S01E01.mkv"), now);
        await File.WriteAllTextAsync(source.Path, "unchanged paired video");
        File.SetLastWriteTimeUtc(source.Path, now.AddMinutes(-1).UtcDateTime);
        db.EntityFiles.Add(source);
        db.EntityLibraryRoots.Add(new EntityLibraryRootRow { EntityId = series.Id, LibraryRootId = rootId });
        db.EntitySources.AddRange(
            new EntitySourceRow { EntityId = series.Id, Code = EntitySourceCode.Folder.ToCode(), Value = Path.GetDirectoryName(folder)! },
            new EntitySourceRow { EntityId = season.Id, Code = EntitySourceCode.Folder.ToCode(), Value = folder });
        var receipt = new AcquisitionRow { Id = Guid.NewGuid(), EntityId = season.Id, Kind = EntityKind.VideoSeason,
            Status = AcquisitionStatus.Imported, SeasonNumber = 1, Title = "Show", TargetLibraryRootId = rootId,
            FinalSourcePath = folder, SelectedReleaseJson = JsonSerializer.Serialize(new SelectedRelease("Show S01", "Indexer", null)),
            CreatedAt = now.AddMinutes(-1), UpdatedAt = now.AddSeconds(1),
            ImportResultJson = AcquisitionImportFileLedgerJson.Serialize(new(AcquisitionImportPhase.Imported, [
                new("pair", "Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv", source.SizeBytes!.Value,
                    "Show.S01E01-E02.Hidden.Garden.&.Mountain.Journey.mkv", Path.GetRelativePath(root, source.Path),
                    AcquisitionImportFileRole.Media, AcquisitionImportContentKind.Video,
                    AcquisitionImportFileStatus.Imported, AcquisitionImportDecision.PlaceNew, null)
            ])) };
        var monitor = new MonitorRow { Id = Guid.NewGuid(), EntityId = season.Id, AcquisitionId = receipt.Id,
            Kind = EntityKind.VideoSeason, Title = season.Title, Status = MonitorStatus.Active, CreatedAt = now, UpdatedAt = now };
        db.Acquisitions.Add(receipt);
        db.Monitors.Add(monitor);
        await db.SaveChangesAsync();
        return new(season, owner, missing, source, receipt, monitor);
    }

    private static EntityRow Episode(Guid seasonId, int number, string title, bool wanted) => new() {
        Id = Guid.NewGuid(), ParentEntityId = seasonId, KindCode = EntityKind.VideoEpisode.ToCode(), Title = title,
        SortOrder = number, IsWanted = wanted, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };
    private static EntityFileRow Source(Guid entityId, string path, DateTimeOffset now) => new() {
        Id = Guid.NewGuid(), EntityId = entityId, Role = EntityFileRole.Source, Path = path, SizeBytes = 22,
        MimeType = "video/x-matroska", CreatedAt = now, UpdatedAt = now
    };
    private static PrismediaDbContext CreateContext() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed record Fixture(EntityRow Season, EntityRow Owner, EntityRow Missing, EntityFileRow Source,
        AcquisitionRow Receipt, MonitorRow Monitor);
    private sealed class BeforeLease(IEntityLifecycleMutationLease inner, Func<Task> before) : IEntityLifecycleMutationLease {
        public Task<bool> ExecuteAsync(Guid entityId, Func<CancellationToken, Task> mutation, CancellationToken cancellationToken) =>
            ExecuteManyAsync([entityId], mutation, cancellationToken);
        public async Task<bool> ExecuteManyAsync(IReadOnlyCollection<Guid> ids, Func<CancellationToken, Task> mutation, CancellationToken token) {
            await before();
            return await inner.ExecuteManyAsync(ids, mutation, token);
        }
    }
}
