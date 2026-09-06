using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfMonitorStoreLibraryBaselineTests {
    [Theory]
    [InlineData(EntityKind.Movie, false)]
    [InlineData(EntityKind.VideoEpisode, false)]
    [InlineData(EntityKind.VideoEpisode, true)]
    public async Task AnUpgradeProfileCanMonitorAnExistingLibraryFileWithoutInventingADownload(EntityKind kind, bool postgres) {
        await using var database = postgres ? await PostgresTestDatabase.CreateAsync() : null;
        await using var db = database?.CreateContext() ?? MemoryContext();
        var root = Directory.CreateTempSubdirectory("prismedia-owned-monitor-").FullName;
        try {
            var fixture = await SeedAsync(db, root, kind);
            Assert.False(Assert.Single(await fixture.Store.ListImmediateForMonitorAsync(fixture.MonitorId, default)).IsUpgrade);
            Assert.Empty(await db.Acquisitions.ToArrayAsync());
            var profile = await db.BookAcquisitionProfiles.SingleAsync();
            profile.UpgradeUntilCutoff = true;
            profile.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            var due = Assert.Single(await fixture.Store.ListImmediateForMonitorAsync(fixture.MonitorId, default));

            Assert.True(due.IsUpgrade);
            var baseline = await db.Acquisitions.SingleAsync();
            Assert.Equal(baseline.Id, due.AcquisitionId);
            Assert.Equal(fixture.EntityId, baseline.EntityId);
            Assert.Equal(fixture.SourcePath, baseline.FinalSourcePath);
            Assert.Equal(AcquisitionStatus.Imported, baseline.Status);
            Assert.Equal(VideoQuality.Unknown.ToCode(), baseline.OwnedMediaQuality);
            Assert.True(baseline.UpgradeQualityCaptured);
            Assert.Null(baseline.SelectedReleaseJson);
            Assert.Empty(await db.DownloadTransfers.ToArrayAsync());
            Assert.Empty(await db.AcquisitionHistory.ToArrayAsync());
            Assert.Equal(fixture.SourceId, (await db.EntityFiles.SingleAsync()).Id);
            Assert.Equal("owned video bytes", await File.ReadAllTextAsync(fixture.SourcePath));
            var child = await fixture.Store.CreateUpgradeChildAsync(fixture.MonitorId, default);
            var acquisitions = AcquisitionTestFactory.Store(db);
            Assert.Equal(720, (await acquisitions.GetUpgradeOwnedQualityAsync(child!.Value, default))!.VideoResolutionTier);
            var input = await acquisitions.GetSearchInputAsync(child.Value, default);
            if (kind == EntityKind.VideoEpisode) {
                Assert.Equal("Show", input!.Series);
                Assert.Equal(2, input.SeasonNumber);
                Assert.Equal(41, input.EpisodeNumber);
            }
            // Sharing discovered after a child was queued must reach the final replacement boundary too.
            var otherOwner = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            db.Entities.Add(new EntityRow { Id = otherOwner, KindCode = kind.ToCode(), Title = "Other owner", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            db.EntityFiles.Add(new EntityFileRow { Id = Guid.NewGuid(), EntityId = otherOwner, Role = EntityFileRole.Source,
                Path = fixture.SourcePath, Source = FileSourceKind.Scan.ToCode(), CreatedAt = now, UpdatedAt = now });
            (await db.Acquisitions.FindAsync(child.Value))!.Status = AcquisitionStatus.Downloaded;
            await db.SaveChangesAsync();
            Assert.True((await acquisitions.GetUpgradeOwnedQualityAsync(child.Value, default))!.VideoSourceShared);
            Assert.True((await acquisitions.GetUpgradeReplaceTargetAsync(child.Value, default))!.ParentVideoSourceShared);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("manual selection")]
    [InlineData("paused")]
    [InlineData("missing file")]
    [InlineData("changed file")]
    [InlineData("shared source")]
    [InlineData("multiple sources")]
    [InlineData("stale probe")]
    [InlineData("failed probe")]
    [InlineData("unknown subtitles")]
    [InlineData("existing attempt")]
    public async Task UncertainOrInactiveOwnershipDoesNotCreateAnUpgradeBaseline(string scenario) {
        await using var db = MemoryContext();
        var root = Directory.CreateTempSubdirectory("prismedia-owned-monitor-guard-").FullName;
        try {
            var fixture = await SeedAsync(db, root, EntityKind.Movie);
            var profile = await db.BookAcquisitionProfiles.SingleAsync();
            profile.UpgradeUntilCutoff = true;
            var now = DateTimeOffset.UtcNow;
            switch (scenario) {
                case "manual selection": profile.AutoPick = false; break;
                case "paused": (await db.Monitors.SingleAsync()).Status = MonitorStatus.Paused; break;
                case "missing file": File.Delete(fixture.SourcePath); break;
                case "changed file": await File.AppendAllTextAsync(fixture.SourcePath, " changed"); break;
                case "shared source":
                case "multiple sources":
                    db.EntityFiles.Add(new EntityFileRow { Id = Guid.NewGuid(),
                        EntityId = scenario == "shared source" ? Guid.NewGuid() : fixture.EntityId,
                        Role = EntityFileRole.Source, Path = fixture.SourcePath, Source = FileSourceKind.Scan.ToCode(),
                        CreatedAt = now, UpdatedAt = now });
                    break;
                case "stale probe": (await db.MediaSources.SingleAsync()).SizeBytes++; break;
                case "failed probe": db.EntityTechnical.Add(new EntityTechnicalRow { EntityId = fixture.EntityId, ProbeFailedAt = now, UpdatedAt = now }); break;
                case "unknown subtitles": db.EntitySubtitleStates.Remove(await db.EntitySubtitleStates.SingleAsync()); break;
                case "existing attempt": db.Acquisitions.Add(new AcquisitionRow { Id = Guid.NewGuid(), EntityId = fixture.EntityId,
                    Kind = EntityKind.Movie, Status = AcquisitionStatus.AwaitingSelection, CreatedAt = now, UpdatedAt = now }); break;
            }
            await db.SaveChangesAsync();
            var previousAttempts = await db.Acquisitions.Select(row => row.Id).ToArrayAsync();

            var due = await fixture.Store.ListImmediateForMonitorAsync(fixture.MonitorId, default);

            Assert.DoesNotContain(due, item => item.IsUpgrade);
            Assert.Equal(previousAttempts, await db.Acquisitions.Select(row => row.Id).ToArrayAsync());
            Assert.Null((await db.Monitors.SingleAsync()).AcquisitionId);
            Assert.Empty(await db.DownloadTransfers.ToArrayAsync());
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<Fixture> SeedAsync(PrismediaDbContext db, string root, EntityKind kind) {
        var now = DateTimeOffset.UtcNow;
        var rootId = Guid.NewGuid(); var profileId = Guid.NewGuid(); var entityId = Guid.NewGuid();
        var monitorId = Guid.NewGuid(); var sourceId = Guid.NewGuid();
        db.LibraryRoots.Add(new LibraryRootRow { Id = rootId, Path = root, Label = "Validation", Enabled = true,
            ScanVideos = true, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        Guid? parentId = null;
        if (kind == EntityKind.VideoEpisode) {
            var seriesId = Guid.NewGuid(); parentId = Guid.NewGuid();
            db.Entities.Add(new EntityRow { Id = seriesId, KindCode = EntityKind.VideoSeries.ToCode(), Title = "Show", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            db.Entities.Add(new EntityRow { Id = parentId.Value, ParentEntityId = seriesId,
                KindCode = EntityKind.VideoSeason.ToCode(), Title = "Season two", SortOrder = 3, CreatedAt = now, UpdatedAt = now });
            db.EntityPositions.Add(new EntityPositionRow { EntityId = parentId.Value, Code = EntityPositionCodes.Season, Value = 2 });
            await db.SaveChangesAsync();
        }
        db.Entities.Add(new EntityRow { Id = entityId, ParentEntityId = parentId, KindCode = kind.ToCode(), Title = "Canonical story",
            SortOrder = kind == EntityKind.VideoEpisode ? 7 : null, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        if (kind == EntityKind.VideoEpisode) db.EntityPositions.Add(new EntityPositionRow { EntityId = entityId, Code = EntityPositionCodes.Episode, Value = 41 });
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow { Id = profileId, Kind = AcquisitionProfileKinds.For(kind),
            DisplayName = "HD", TargetLibraryRootId = rootId, AutoPick = true, CutoffQuality = VideoQuality.Webdl1080p.ToCode(), CreatedAt = now, UpdatedAt = now });
        var path = Path.Combine(root, "owned.mkv");
        await File.WriteAllTextAsync(path, "owned video bytes");
        var size = new FileInfo(path).Length;
        db.EntityFiles.Add(new EntityFileRow { Id = sourceId, EntityId = entityId, Role = EntityFileRole.Source,
            Path = path, SizeBytes = size, Source = FileSourceKind.Scan.ToCode(), CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        db.MediaSources.Add(new MediaSourceRow { Id = Guid.NewGuid(), EntityId = entityId, EntityFileId = sourceId,
            Path = path, SizeBytes = size, Width = 1280, Height = 720, DurationSeconds = 1200, CreatedAt = now, UpdatedAt = now });
        db.EntitySubtitleStates.Add(new EntitySubtitleStateRow { EntityId = entityId, SubtitlesExtractedAt = now });
        db.EntitySubtitles.Add(new EntitySubtitleRow { Id = Guid.NewGuid(), EntityId = entityId, Language = "eng", Format = "vtt",
            Source = EntitySubtitleSource.Embedded, SourceKey = "stream:2", StoragePath = "/data/subtitles/2.vtt", SourceFormat = "subrip", CreatedAt = now });
        db.Monitors.Add(new MonitorRow { Id = monitorId, EntityId = entityId, Kind = kind, Title = "Canonical story", Status = MonitorStatus.Active,
            ProfileId = profileId, TargetLibraryRootId = rootId, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        return new(new EfMonitorStore(db), monitorId, entityId, sourceId, path);
    }

    private static PrismediaDbContext MemoryContext() => new(new DbContextOptionsBuilder<PrismediaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed record Fixture(EfMonitorStore Store, Guid MonitorId, Guid EntityId, Guid SourceId, string SourcePath);
}
