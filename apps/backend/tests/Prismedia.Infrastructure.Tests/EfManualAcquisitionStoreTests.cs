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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ScannedEpisodeReplacementUsesCurrentCatalogCoordinates(bool directlyUnderSeries, bool explicitDefaultProfile) {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "episode.avi");
        await File.WriteAllTextAsync(path, "existing episode");
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var series = Guid.NewGuid(); var season = Guid.NewGuid(); var episode = Guid.NewGuid();
        var profile = Guid.NewGuid();
        db.Entities.AddRange(
            new EntityRow { Id = series, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Example Show", CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = season, ParentEntityId = series, KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season 2", SortOrder = 50, CreatedAt = now, UpdatedAt = now },
            new EntityRow { Id = episode, ParentEntityId = directlyUnderSeries ? series : season, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "A Bug Adventure", SortOrder = 99, CreatedAt = now, UpdatedAt = now });
        db.EntityPositions.AddRange(
            new EntityPositionRow { EntityId = episode, Code = EntityPositionCodes.Season, Value = 2, UpdatedAt = now },
            new EntityPositionRow { EntityId = episode, Code = EntityPositionCodes.Episode, Value = 17, UpdatedAt = now },
            new EntityPositionRow { EntityId = episode, Code = EntityPositionCodes.AbsoluteEpisode, Value = 99, UpdatedAt = now },
            new EntityPositionRow { EntityId = season, Code = EntityPositionCodes.Season, Value = 2, UpdatedAt = now });
        db.EntityFiles.Add(new EntityFileRow { Id = Guid.NewGuid(), EntityId = episode, Role = EntityFileRole.Source, Path = path, SizeBytes = 16, CreatedAt = now, UpdatedAt = now });
        db.EntityDates.Add(new EntityDateRow { EntityId = series, Code = EntityDateType.FirstAir.ToCode(), Value = "1997-01-01", SortableValue = new DateOnly(1997, 1, 1), UpdatedAt = now });
        db.Monitors.Add(new MonitorRow { Id = Guid.NewGuid(), EntityId = series, Kind = EntityKind.VideoSeries, ProfileId = profile, Status = MonitorStatus.Paused, Title = "Example Show", CreatedAt = now, UpdatedAt = now });
        if (explicitDefaultProfile) db.Monitors.Add(new MonitorRow { Id = Guid.NewGuid(), EntityId = episode, Kind = EntityKind.VideoEpisode, ProfileId = null, Status = MonitorStatus.Paused, Title = "A Bug Adventure", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        var store = new EfManualAcquisitionStore(db, AcquisitionTestFactory.Store(db), new EfImportTargetIndex(db));

        var target = await store.GetSearchTargetAsync(episode, default);

        Assert.NotNull(target);
        Assert.Equal("Example Show", target.Input.Series);
        Assert.Equal(2, target.Input.SeasonNumber);
        Assert.Equal(17, target.Input.EpisodeNumber);
        Assert.Equal(99, target.Input.AbsoluteEpisodeNumber);
        Assert.Equal(1997, target.Input.Year);
        Assert.Equal(explicitDefaultProfile ? null : (Guid?)profile, target.Input.ProfileId);
        if (!directlyUnderSeries) Assert.Equal(episode, Assert.Single(Assert.Single(target.Input.EpisodeCatalog).Episodes).EntityId);
        Assert.Empty(db.Acquisitions);
        var childId = await store.CreateReviewedReplacementAsync(episode, [], default);
        var child = await db.Acquisitions.SingleAsync(row => row.Id == childId);
        Assert.Equal(target.Input.ProfileId, child.ProfileId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EpisodeReplacementCannotConsumeAFileOwnedByAnotherEpisode(bool hasReceipt) {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "combined.mkv");
        await File.WriteAllTextAsync(path, "two episodes together");
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow; var episode = Guid.NewGuid(); var other = Guid.NewGuid();
        foreach (var id in new[] { episode, other }) {
            db.Entities.Add(new EntityRow { Id = id, KindCode = EntityKind.VideoEpisode.ToCode(), Title = "Example episode", CreatedAt = now, UpdatedAt = now });
            db.EntityFiles.Add(new EntityFileRow { Id = Guid.NewGuid(), EntityId = id, Role = EntityFileRole.Source, Path = path, CreatedAt = now, UpdatedAt = now });
        }
        if (hasReceipt) db.Acquisitions.Add(new AcquisitionRow { Id = Guid.NewGuid(), EntityId = episode, Kind = EntityKind.VideoEpisode,
            Status = AcquisitionStatus.Imported, Title = "Example episode", FinalSourcePath = path, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        var store = new EfManualAcquisitionStore(db, AcquisitionTestFactory.Store(db));

        Assert.Null(await store.GetSearchTargetAsync(episode, default));
        Assert.Null(await store.CreateReviewedReplacementAsync(episode, [], default));
    }

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
        var store = new EfManualAcquisitionStore(db, AcquisitionTestFactory.Store(db));

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

    [Fact]
    public async Task ReplayingTheSameReviewedCandidatesReturnsTheExistingUpgradeChild() {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, "Hamilton.epub");
        await File.WriteAllTextAsync(sourcePath, "owned");
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var entityId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = entityId,
            KindCode = EntityKind.Book.ToCode(),
            Title = "Hamilton",
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
        var candidate = new ReviewedReleaseCandidate(
            Guid.NewGuid(),
            new ScoredRelease(
                new IndexerRelease(
                    "Hamilton album",
                    1_000,
                    1,
                    0,
                    DownloadProtocol.Soulseek,
                    "slskd://reviewed-release",
                    null,
                    null,
                    null,
                    null,
                    null),
                null,
                "Soulseek",
                true,
                100,
                []));
        var store = new EfManualAcquisitionStore(db, AcquisitionTestFactory.Store(db));

        var first = await store.CreateReviewedReplacementAsync(
            entityId,
            [candidate],
            CancellationToken.None);
        var replay = await store.CreateReviewedReplacementAsync(
            entityId,
            [candidate],
            CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Single(await db.Acquisitions.AsNoTracking()
            .Where(row => row.UpgradeOfAcquisitionId != null)
            .ToArrayAsync());
        Assert.Single(await db.ReleaseCandidates.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task WaitingDownloadClientAttemptCannotBeReplacedByAManualUpload() {
        await using var db = CreateContext();
        var acquisitionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId,
            Status = AcquisitionStatus.WaitingForDownloadClient,
            Title = "Waiting book",
            ExternalIdsJson = "{}",
            SourceUrlsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            ClientItemId = "existing-transfer",
            Progress = 0.4,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var store = new EfManualAcquisitionStore(db, AcquisitionTestFactory.Store(db));

        var completed = await store.CompleteAsync(
            acquisitionId,
            new CompletedAcquisitionUpload("manual-upload", "/upload", "book.epub"),
            CancellationToken.None);

        Assert.False(completed);
        Assert.Contains(
            await db.DownloadTransfers.AsNoTracking().ToArrayAsync(),
            transfer => transfer.ClientItemId == "existing-transfer");
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    public void Dispose() {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
