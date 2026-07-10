using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// End-to-end coverage of the TV import engine's merged path: an acquisition linked to a series that
/// already lives on disk places new episodes into the EXISTING folders (no duplicate series folder),
/// replaces strictly-better collisions in place, drops non-upgrades, and — when nothing was usable —
/// fails the acquisition with the release blocklisted. The legacy template path stays untouched for
/// acquisitions without an entity link.
/// </summary>
public sealed class TvAcquisitionImportEngineTests : IDisposable {
    private readonly string _workRoot = Directory.CreateTempSubdirectory("prismedia-tv-import-").FullName;

    public void Dispose() {
        try {
            Directory.Delete(_workRoot, recursive: true);
        } catch {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public async Task MergesNewEpisodeIntoTheExistingSeasonFolder() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"], releaseTitle: "Show S01 1080p WEB-DL");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv")));
        Assert.True(File.Exists(harness.OwnedEpisodePath)); // the owned episode is untouched
        Assert.DoesNotContain(
            Directory.GetDirectories(_workRoot),
            dir => dir.Contains("Season", StringComparison.Ordinal)); // no template-derived parallel series folder
        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
        Assert.Empty(await db.AcquisitionBlocklist.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task ImportedMeansWantedEpisodeIsImmediatelySourceBackedWithoutRunningAScanJob() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"], releaseTitle: "Show S01 1080p WEB-DL");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        var wantedEpisode = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == harness.WantedEpisodeId);
        var source = await db.EntityFiles.AsNoTracking()
            .SingleAsync(row => row.EntityId == harness.WantedEpisodeId && row.Role == EntityFileRole.Source);

        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
        Assert.False(wantedEpisode.IsWanted);
        Assert.Equal(Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv"), source.Path);
        Assert.True(await db.VideoDetails.AsNoTracking().AnyAsync(row => row.EntityId == harness.WantedEpisodeId));
        Assert.Single(await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == harness.SeasonId && row.KindCode == EntityKindRegistry.Video.Code && row.SortOrder == 2)
            .ToArrayAsync());
    }

    [Fact]
    public async Task DeletedWantedSeasonIsReboundBeforeImportedWhileMissingEpisodesStayWanted() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"],
            releaseTitle: "Show S01 1080p WEB-DL",
            deletedSeason: true);

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        var season = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == harness.SeasonId);
        var episodes = await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == harness.SeasonId && row.KindCode == EntityKindRegistry.Video.Code)
            .OrderBy(row => row.SortOrder)
            .ToArrayAsync();
        var episodeOne = Assert.Single(episodes, row => row.SortOrder == 1);
        var episodeTwo = Assert.Single(episodes, row => row.SortOrder == 2);

        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
        Assert.False(season.IsWanted);
        Assert.True(episodeOne.IsWanted);
        Assert.False(episodeTwo.IsWanted);
        Assert.True(await db.EntityFiles.AsNoTracking().AnyAsync(row =>
            row.EntityId == season.Id && row.Role == EntityFileRole.Source && row.Path == harness.SeasonFolder));
        Assert.False(await db.EntityFiles.AsNoTracking().AnyAsync(row => row.EntityId == episodeOne.Id));
        Assert.True(await db.EntityFiles.AsNoTracking().AnyAsync(row =>
            row.EntityId == episodeTwo.Id && row.Role == EntityFileRole.Source && row.Path == Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv")));
    }

    [Fact]
    public async Task MaterializationFailureNeverRecordsImported() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"],
            releaseTitle: "Show S01 1080p WEB-DL",
            failMaterialization: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None));

        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
        Assert.DoesNotContain(
            await db.AcquisitionHistory.AsNoTracking().Select(row => row.Event).ToArrayAsync(),
            value => value == AcquisitionHistoryEvent.Imported);
    }

    [Fact]
    public async Task RetryCatalogsThePlacedCheckpointWhenMoveConsumedTheDownloadPayload() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"],
            releaseTitle: "Show S01 1080p WEB-DL",
            failMaterialization: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None));
        var resumeImport = await AcquisitionTestFactory.Store(db)
            .GetImportContextAsync(harness.Import.Id, CancellationToken.None);

        Assert.NotNull(resumeImport?.TvImportCheckpoint);
        Assert.All(resumeImport!.TvImportCheckpoint!.Units, unit => Assert.NotNull(unit.FinalPath));
        Assert.False(File.Exists(Path.Combine(harness.Import.ContentPath!, "Show.S01E02.1080p.WEB-DL.mkv")));
        await harness.ResumeEngine.ImportAsync(
            harness.Context,
            resumeImport,
            CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
        Assert.Null((await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == harness.Import.Id)).ImportCheckpointJson);
        Assert.True(await db.EntityFiles.AsNoTracking().AnyAsync(row =>
            row.EntityId == harness.WantedEpisodeId && row.Role == EntityFileRole.Source));
    }

    [Fact]
    public async Task PartialSeasonPackResumesEveryOriginalUnitFromTheDurableCheckpoint() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: [
                "Show.S01E02.1080p.WEB-DL.mkv",
                "Show.S01E03.1080p.WEB-DL.mkv",
                "Show.S01E04.1080p.WEB-DL.mkv"
            ],
            releaseTitle: "Show S01 1080p WEB-DL",
            wantedEpisodeNumbers: [2, 3, 4],
            failPlacementOnCall: 3);

        await Assert.ThrowsAsync<IOException>(() =>
            harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None));
        var resumeImport = await AcquisitionTestFactory.Store(db)
            .GetImportContextAsync(harness.Import.Id, CancellationToken.None);

        Assert.NotNull(resumeImport?.TvImportCheckpoint);
        Assert.Equal(2, resumeImport!.TvImportCheckpoint!.Units.Count(unit => unit.FinalPath is not null));
        Assert.Single(resumeImport.TvImportCheckpoint.Units, unit => unit.FinalPath is null);
        var checkpointJson = await db.Acquisitions.AsNoTracking()
            .Where(row => row.Id == harness.Import.Id)
            .Select(row => row.ImportCheckpointJson)
            .SingleAsync();
        Assert.Contains("\"ImportMode\":\"move\"", checkpointJson);

        await harness.ResumeEngine.ImportAsync(harness.Context, resumeImport, CancellationToken.None);

        var readyEpisodes = await db.Entities.AsNoTracking()
            .Where(episode => episode.ParentEntityId == harness.SeasonId
                && episode.KindCode == EntityKindRegistry.Video.Code
                && episode.SortOrder >= 2
                && episode.SortOrder <= 4)
            .OrderBy(episode => episode.SortOrder)
            .ToArrayAsync();
        Assert.Equal<int?>([2, 3, 4], readyEpisodes.Select(episode => episode.SortOrder));
        Assert.All(readyEpisodes, episode => Assert.False(episode.IsWanted));
        Assert.All(readyEpisodes, episode => Assert.True(db.EntityFiles.AsNoTracking().Any(row =>
            row.EntityId == episode.Id && row.Role == EntityFileRole.Source)));
        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
        Assert.Null((await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == harness.Import.Id)).ImportCheckpointJson);
    }

    [Fact]
    public async Task SupersedingReleaseCannotAbandonPartialNewEpisodesThatAScanMayHaveBound() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: [
                "Show.S01E02.1080p.WEB-DL.mkv",
                "Show.S01E03.1080p.WEB-DL.mkv"
            ],
            releaseTitle: "Show S01 1080p WEB-DL",
            wantedEpisodeNumbers: [2, 3],
            failPlacementOnCall: 2);
        await Assert.ThrowsAsync<IOException>(() =>
            harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None));
        var store = AcquisitionTestFactory.Store(db);
        var resumeImport = await store.GetImportContextAsync(harness.Import.Id, CancellationToken.None);
        var placed = Assert.Single(resumeImport!.TvImportCheckpoint!.Units, unit => unit.FinalPath is not null).FinalPath!;
        Assert.True(File.Exists(placed));

        var abandoned = await TvImportCheckpointLifecycle.TryAbandonAsync(
            store,
            resumeImport,
            CancellationToken.None);

        Assert.False(abandoned);
        Assert.True(File.Exists(placed));
        var row = await db.Acquisitions.AsNoTracking().SingleAsync(value => value.Id == harness.Import.Id);
        Assert.NotNull(row.ImportCheckpointJson);
        Assert.Null(row.FinalSourcePath);
        Assert.NotEmpty(await db.AcquisitionImportHints.AsNoTracking()
            .Where(hint => hint.AcquisitionId == harness.Import.Id)
            .ToArrayAsync());
    }

    [Fact]
    public async Task SupersedingReleaseCanClearAnUntouchedCheckpoint() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"],
            releaseTitle: "Show S01 1080p WEB-DL",
            failPlacementOnCall: 1);
        await Assert.ThrowsAsync<IOException>(() =>
            harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None));
        var store = AcquisitionTestFactory.Store(db);
        await store.SetStatusAsync(
            harness.Import.Id,
            AcquisitionStatus.Failed,
            "Synthetic placement failure.",
            CancellationToken.None);
        var resumeImport = await store.GetImportContextAsync(harness.Import.Id, CancellationToken.None);

        var abandoned = await TvImportCheckpointLifecycle.TryAbandonAsync(
            store,
            resumeImport!,
            CancellationToken.None);

        Assert.True(abandoned);
        Assert.Null((await db.Acquisitions.AsNoTracking()
            .SingleAsync(value => value.Id == harness.Import.Id)).ImportCheckpointJson);
        Assert.True(File.Exists(Path.Combine(
            harness.Import.ContentPath!,
            "Show.S01E02.1080p.WEB-DL.mkv")));
    }

    [Fact]
    public async Task SupersedingReleaseCannotAbandonAReplacementThatAlreadyStarted() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E01.2160p.BluRay.mkv"],
            releaseTitle: "Show S01 2160p BluRay",
            failSameFormatAfterStage: true);
        await Assert.ThrowsAsync<IOException>(() =>
            harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None));
        var store = AcquisitionTestFactory.Store(db);
        var resumeImport = await store.GetImportContextAsync(harness.Import.Id, CancellationToken.None);

        var abandoned = await TvImportCheckpointLifecycle.TryAbandonAsync(
            store,
            resumeImport!,
            CancellationToken.None);

        Assert.False(abandoned);
        Assert.NotNull((await db.Acquisitions.AsNoTracking()
            .SingleAsync(value => value.Id == harness.Import.Id)).ImportCheckpointJson);
        Assert.True(File.Exists(OwnedFileReplacementArtifacts.StagedPath(harness.OwnedEpisodePath)));
    }

    [Fact]
    public async Task CheckpointFromAPriorTransferIsNeverAppliedToANewDownloadAttempt() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"],
            releaseTitle: "Show S01 1080p WEB-DL",
            failMaterialization: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None));
        var resumeImport = await AcquisitionTestFactory.Store(db)
            .GetImportContextAsync(harness.Import.Id, CancellationToken.None);

        await harness.ResumeEngine.ImportAsync(
            harness.Context,
            resumeImport! with { ClientItemId = "different-download-item" },
            CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.NotNull((await db.Acquisitions.AsNoTracking()
            .SingleAsync(row => row.Id == harness.Import.Id)).ImportCheckpointJson);
    }

    [Fact]
    public async Task ExactCheckpointRecoversThePlacedFileAfterACrashWindow() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"],
            releaseTitle: "Show S01 1080p WEB-DL",
            payloadContent: "new-episode",
            failAfterPlacementOnCall: 1);

        await Assert.ThrowsAsync<IOException>(() =>
            harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None));
        var resumeImport = await AcquisitionTestFactory.Store(db)
            .GetImportContextAsync(harness.Import.Id, CancellationToken.None);

        var checkpoint = Assert.IsType<TvImportCheckpoint>(resumeImport?.TvImportCheckpoint);
        var unit = Assert.Single(checkpoint.Units);
        var exactTarget = Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv");
        Assert.Equal(exactTarget, unit.TargetAbsolutePath);
        Assert.Null(unit.FinalPath);
        Assert.Equal("new-episode", await File.ReadAllTextAsync(exactTarget));
        Assert.False(File.Exists(Path.Combine(harness.Import.ContentPath!, "Show.S01E02.1080p.WEB-DL.mkv")));

        await harness.ResumeEngine.ImportAsync(harness.Context, resumeImport!, CancellationToken.None);

        var source = await db.EntityFiles.AsNoTracking()
            .SingleAsync(row => row.EntityId == harness.WantedEpisodeId && row.Role == EntityFileRole.Source);
        Assert.Equal(exactTarget, source.Path);
        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
    }

    [Fact]
    public async Task OccupiedEpisodeSlotIsHeldWithoutCreatingASuffixedDuplicate() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"],
            releaseTitle: "Show S01 1080p WEB-DL",
            payloadContent: "new-episode",
            preexistingTargetContent: "unrelated-existing-file");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Equal(
            "unrelated-existing-file",
            await File.ReadAllTextAsync(Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv")));
        Assert.Equal(
            "new-episode",
            await File.ReadAllTextAsync(Path.Combine(harness.Import.ContentPath!, "Show.S01E02.1080p.WEB-DL.mkv")));
        Assert.False(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E02 (2).mkv")));
    }

    [Fact]
    public async Task MissingSeasonFolderIsCreatedInsideTheExistingSeriesFolder() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S02E01.1080p.WEB-DL.mkv"], releaseTitle: "Show S02 1080p WEB-DL");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(harness.SeriesFolder, "Season 02", "Show - S02E01.mkv")));
        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
    }

    [Fact]
    public async Task StrictUpgradeReplacesTheOwnedFileInPlace() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S01E01.1080p.BluRay.mkv"], releaseTitle: "Show S01 1080p BluRay", payloadContent: "upgraded-bytes");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
        // Same path, new content; the previous file is preserved as the recoverable backup.
        Assert.Equal("upgraded-bytes", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        Assert.Single(Directory.GetFiles(
            Path.GetDirectoryName(harness.OwnedEpisodePath)!,
            Path.GetFileName(harness.OwnedEpisodePath) + ".prismedia-bak-*"));
    }

    [Fact]
    public async Task SamePathUpgradeInvalidatesOldProbeAndGeneratedAssetState() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E01.1080p.BluRay.mkv"],
            releaseTitle: "Show S01 1080p BluRay",
            payloadContent: "upgraded-video-bytes",
            autoGenerateMetadata: true);
        var now = DateTimeOffset.UtcNow;
        db.EntityTechnical.Add(new EntityTechnicalRow {
            EntityId = harness.OwnedEpisodeId,
            ProbeFailedAt = now,
            UpdatedAt = now,
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = harness.OwnedEpisodeId,
            Role = EntityFileRole.Thumbnail,
            Path = "/cache/old-thumbnail.jpg",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.TrickplayInfos.Add(new TrickplayInfoRow {
            EntityId = harness.OwnedEpisodeId,
            Width = 320,
            Height = 180,
            TileWidth = 5,
            TileHeight = 5,
            ThumbnailCount = 10,
            IntervalSeconds = 10,
            Bandwidth = 1000,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.False(await db.EntityTechnical.AsNoTracking().AnyAsync(row => row.EntityId == harness.OwnedEpisodeId));
        Assert.False(await db.EntityFiles.AsNoTracking().AnyAsync(row =>
            row.EntityId == harness.OwnedEpisodeId && row.Role == EntityFileRole.Thumbnail));
        Assert.False(await db.TrickplayInfos.AsNoTracking().AnyAsync(row => row.EntityId == harness.OwnedEpisodeId));
        var source = await db.EntityFiles.AsNoTracking().SingleAsync(row =>
            row.EntityId == harness.OwnedEpisodeId && row.Role == EntityFileRole.Source);
        Assert.Equal(new FileInfo(harness.OwnedEpisodePath).Length, source.SizeBytes);
        Assert.Contains(harness.Queue.Enqueued, request =>
            request.Type == JobType.ProbeVideo
            && request.TargetEntityId == harness.OwnedEpisodeId.ToString());
    }

    [Fact]
    public async Task NothingBetterFailsBlocklistsAndRecordsHistory() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 1080p BluRay.mkv", payloadFiles: ["Show.S01E01.720p.WEB.mkv"], releaseTitle: "Show S01 720p WEB");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Failed, await StatusOf(db, harness.Import.Id));
        var blocked = Assert.Single(await db.AcquisitionBlocklist.AsNoTracking().ToArrayAsync());
        Assert.Equal(BlocklistReason.NoImportableFiles, blocked.Reason);
        var events = await db.AcquisitionHistory.AsNoTracking().Select(row => row.Event).ToArrayAsync();
        Assert.Contains(AcquisitionHistoryEvent.Blocklisted, events);
        Assert.Contains(AcquisitionHistoryEvent.ImportFailed, events);
        // The non-upgrade payload was not placed anywhere in the series tree.
        Assert.False(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E01.mkv")) && new FileInfo(harness.OwnedEpisodePath).Length == 0);
    }

    [Fact]
    public async Task FormatChangeUpgradeIsHeldForManualImportWithoutBlocklisting() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mp4", payloadFiles: ["Show.S01E01.2160p.BluRay.mkv"], releaseTitle: "Show S01 2160p BluRay");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Empty(await db.AcquisitionBlocklist.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task ConsentedFormatChangePreservesTheEpisodeEntityAndRebindsItsSource() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mp4",
            payloadFiles: ["Show.S01E01.2160p.BluRay.mkv"],
            releaseTitle: "Show S01 2160p BluRay");

        await harness.Engine.ImportAsync(
            harness.Context,
            harness.Import with { AllowFormatChange = true },
            CancellationToken.None);

        var episodes = await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == harness.SeasonId && row.KindCode == EntityKindRegistry.Video.Code && row.SortOrder == 1)
            .ToArrayAsync();
        var source = await db.EntityFiles.AsNoTracking()
            .SingleAsync(row => row.EntityId == harness.OwnedEpisodeId && row.Role == EntityFileRole.Source);

        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
        Assert.Single(episodes);
        Assert.Equal(harness.OwnedEpisodeId, episodes[0].Id);
        Assert.Equal(Path.ChangeExtension(harness.OwnedEpisodePath, ".mkv"), source.Path);
        Assert.True(File.Exists(source.Path));
        Assert.False(File.Exists(harness.OwnedEpisodePath));
    }

    [Fact]
    public async Task ConsentedFormatChangeNeverOverwritesAnOccupiedSiblingTarget() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mp4",
            payloadFiles: ["Show.S01E01.2160p.BluRay.mkv"],
            releaseTitle: "Show S01 2160p BluRay",
            payloadContent: "incoming-upgrade");
        var occupiedTarget = Path.ChangeExtension(harness.OwnedEpisodePath, ".mkv");
        await File.WriteAllTextAsync(occupiedTarget, "keep-this-existing-file");

        await harness.Engine.ImportAsync(
            harness.Context,
            harness.Import with { AllowFormatChange = true },
            CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Equal("owned-bytes", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        Assert.Equal("keep-this-existing-file", await File.ReadAllTextAsync(occupiedTarget));
        Assert.Equal(
            "incoming-upgrade",
            await File.ReadAllTextAsync(Path.Combine(harness.Import.ContentPath!, "Show.S01E01.2160p.BluRay.mkv")));
        Assert.Null((await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == harness.Import.Id)).ImportCheckpointJson);
    }

    [Fact]
    public async Task CrossFormatCheckpointResumeRetiresTheOldExtensionBeforeImported() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mp4",
            payloadFiles: ["Show.S01E01.2160p.BluRay.mkv"],
            releaseTitle: "Show S01 2160p BluRay",
            payloadContent: "incoming-upgrade",
            failCrossFormatAfterInstall: true);

        await Assert.ThrowsAsync<IOException>(() => harness.Engine.ImportAsync(
            harness.Context,
            harness.Import with { AllowFormatChange = true },
            CancellationToken.None));
        var replacementPath = Path.ChangeExtension(harness.OwnedEpisodePath, ".mkv");
        Assert.True(File.Exists(harness.OwnedEpisodePath));
        Assert.Equal("incoming-upgrade", await File.ReadAllTextAsync(replacementPath));
        var resumeImport = await AcquisitionTestFactory.Store(db)
            .GetImportContextAsync(harness.Import.Id, CancellationToken.None);

        await harness.ResumeEngine.ImportAsync(harness.Context, resumeImport!, CancellationToken.None);

        Assert.False(File.Exists(harness.OwnedEpisodePath));
        Assert.True(File.Exists(replacementPath));
        var source = await db.EntityFiles.AsNoTracking()
            .SingleAsync(row => row.EntityId == harness.OwnedEpisodeId && row.Role == EntityFileRole.Source);
        Assert.Equal(replacementPath, source.Path);
        Assert.Single(await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == harness.SeasonId
                && row.KindCode == EntityKindRegistry.Video.Code
                && row.SortOrder == 1)
            .ToArrayAsync());
        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
    }

    [Fact]
    public async Task SameFormatCheckpointResumeCompletesAStagedReplacementInsteadOfAdoptingOldBytes() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E01.2160p.BluRay.mkv"],
            releaseTitle: "Show S01 2160p BluRay",
            payloadContent: "incoming-upgrade",
            failSameFormatAfterStage: true);

        await Assert.ThrowsAsync<IOException>(() =>
            harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None));
        Assert.Equal("owned-bytes", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        Assert.True(File.Exists(OwnedFileReplacementArtifacts.StagedPath(harness.OwnedEpisodePath)));
        Assert.False(File.Exists(Path.Combine(harness.Import.ContentPath!, "Show.S01E01.2160p.BluRay.mkv")));
        var resumeImport = await AcquisitionTestFactory.Store(db)
            .GetImportContextAsync(harness.Import.Id, CancellationToken.None);
        var attemptId = Assert.IsType<TvImportCheckpoint>(resumeImport?.TvImportCheckpoint).AttemptId;

        await harness.ResumeEngine.ImportAsync(harness.Context, resumeImport!, CancellationToken.None);

        Assert.Equal("incoming-upgrade", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        Assert.False(File.Exists(OwnedFileReplacementArtifacts.StagedPath(harness.OwnedEpisodePath)));
        Assert.True(File.Exists(OwnedFileReplacementArtifacts.CheckpointBackupPath(
            harness.OwnedEpisodePath,
            attemptId)));
        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
    }

    [Fact]
    public async Task SameFormatCheckpointUsesIncomingEvidenceWhenInstallFailedAfterBackup() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E01.2160p.BluRay.mkv"],
            releaseTitle: "Show S01 2160p BluRay",
            payloadContent: "incoming-upgrade",
            failAfterReplacementEvidence: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None));
        Assert.Equal("owned-bytes", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        var resumeImport = await AcquisitionTestFactory.Store(db)
            .GetImportContextAsync(harness.Import.Id, CancellationToken.None);
        var attemptId = Assert.IsType<TvImportCheckpoint>(resumeImport?.TvImportCheckpoint).AttemptId;
        var evidence = OwnedFileReplacementArtifacts.CheckpointEvidencePath(
            harness.OwnedEpisodePath,
            attemptId);
        Assert.Equal("incoming-upgrade", await File.ReadAllTextAsync(evidence));

        await harness.ResumeEngine.ImportAsync(harness.Context, resumeImport!, CancellationToken.None);

        Assert.Equal("incoming-upgrade", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        Assert.False(File.Exists(evidence));
        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
    }

    [Fact]
    public async Task AcquisitionWithoutEntityLinkKeepsTheTemplatePlacement() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"], releaseTitle: "Show S01 1080p WEB-DL", linkEntity: false);

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        // Template path: a fresh series folder under the library root, not the existing tree.
        Assert.True(File.Exists(Path.Combine(harness.LibraryRoot, "Show", "Season 01", "Show - S01E02.mkv")));
        Assert.False(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv")));
        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
        Assert.True(await db.EntityFiles.AsNoTracking().AnyAsync(row =>
            row.Role == EntityFileRole.Source && row.Path == Path.Combine(harness.LibraryRoot, "Show", "Season 01", "Show - S01E02.mkv")));
    }

    private sealed record Harness(
        TvAcquisitionImportEngine Engine,
        TvAcquisitionImportEngine ResumeEngine,
        JobContext Context,
        AcquisitionImportContext Import,
        string LibraryRoot,
        string SeriesFolder,
        string SeasonFolder,
        string OwnedEpisodePath,
        Guid SeasonId,
        Guid OwnedEpisodeId,
        Guid WantedEpisodeId,
        MergedImportTestSupport.RecordingJobQueue Queue);

    /// <summary>
    /// One seeded world: a library root, an on-disk series (`Show (2008)/S01/<ownedEpisodeName>`) mirrored
    /// as series/season/episode entities with Source files, an acquisition linked to the season, and a
    /// payload folder holding <paramref name="payloadFiles"/>.
    /// </summary>
    private async Task<Harness> HarnessAsync(
        PrismediaDbContext db,
        string ownedEpisodeName,
        string[] payloadFiles,
        string releaseTitle,
        string payloadContent = "payload-bytes",
        bool linkEntity = true,
        bool deletedSeason = false,
        bool failMaterialization = false,
        IReadOnlyList<int>? wantedEpisodeNumbers = null,
        string? preexistingTargetContent = null,
        int? failPlacementOnCall = null,
        int? failAfterPlacementOnCall = null,
        bool failCrossFormatAfterInstall = false,
        bool failSameFormatAfterStage = false,
        bool failAfterReplacementEvidence = false,
        bool autoGenerateMetadata = false) {
        var libraryRoot = Directory.CreateDirectory(Path.Combine(_workRoot, "library")).FullName;
        var seriesFolder = Directory.CreateDirectory(Path.Combine(libraryRoot, "Show (2008)")).FullName;
        var seasonFolder = Directory.CreateDirectory(Path.Combine(seriesFolder, deletedSeason ? "Season 01" : "S01")).FullName;
        var ownedEpisodePath = Path.Combine(seasonFolder, ownedEpisodeName);
        if (!deletedSeason) {
            await File.WriteAllTextAsync(ownedEpisodePath, "owned-bytes");
        }
        if (preexistingTargetContent is not null) {
            await File.WriteAllTextAsync(
                Path.Combine(seasonFolder, "Show - S01E02.mkv"),
                preexistingTargetContent);
        }

        var payloadRoot = Directory.CreateDirectory(Path.Combine(_workRoot, "download", "release")).FullName;
        foreach (var file in payloadFiles) {
            await File.WriteAllTextAsync(Path.Combine(payloadRoot, file), payloadContent);
        }

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();
        var seriesId = AddEntity(db, EntityKindRegistry.VideoSeries.Code, null, null, seriesFolder);
        var seasonId = deletedSeason
            ? AddWantedEntity(db, EntityKindRegistry.VideoSeason.Code, seriesId, 1)
            : AddEntity(db, EntityKindRegistry.VideoSeason.Code, seriesId, 1, seasonFolder);
        Guid ownedEpisodeId;
        if (deletedSeason) {
            ownedEpisodeId = Guid.Empty;
            AddWantedEntity(db, EntityKindRegistry.Video.Code, seasonId, 1);
        } else {
            ownedEpisodeId = AddEntity(db, EntityKindRegistry.Video.Code, seasonId, 1, ownedEpisodePath);
        }
        var wantedIds = (wantedEpisodeNumbers ?? [2])
            .Distinct()
            .ToDictionary(
                episodeNumber => episodeNumber,
                episodeNumber => AddWantedEntity(db, EntityKindRegistry.Video.Code, seasonId, episodeNumber));
        var wantedEpisodeId = wantedIds.GetValueOrDefault(2);

        var acquisitionId = Guid.NewGuid();
        db.Acquisitions.Add(new AcquisitionRow {
            Id = acquisitionId, Status = AcquisitionStatus.Importing, Title = "Show", Series = "Show",
            Kind = EntityKind.VideoSeason, SeasonNumber = 1, EntityId = linkEntity ? seasonId : null,
            ImportClaimJobId = jobId,
            ExternalIdsJson = "{}", SourceUrlsJson = "[]", CreatedAt = now, UpdatedAt = now
        });
        db.DownloadTransfers.Add(new DownloadTransferRow {
            Id = Guid.NewGuid(),
            AcquisitionId = acquisitionId,
            ClientItemId = string.Empty,
            ContentPath = payloadRoot,
            Progress = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var store = AcquisitionTestFactory.Store(db);
        await store.SetSelectedReleaseAsync(acquisitionId, new SelectedRelease(releaseTitle, "Indexer", "hash-1"), CancellationToken.None);

        var import = new AcquisitionImportContext(
            acquisitionId, "Show", Author: null, Series: "Show", Year: null, PosterUrl: null,
            ExternalIdentity: null, ProfileId: null, ContentPath: payloadRoot,
            ClientItemId: null, DownloadClientConfigId: null, Kind: EntityKind.VideoSeason,
            SeasonNumber: 1, EntityId: linkEntity ? seasonId : null);

        var history = new EfAcquisitionHistoryStore(db);
        var rootPersistence = new MergedImportTestSupport.SingleRootPersistence(
            libraryRoot,
            autoGenerateMetadata);
        var scanPersistence = new LibraryScanPersistenceService(db);
        var hints = new AcquisitionHintApplier(db);
        var realMaterializer = new ImportedVideoMaterializer(
            scanPersistence,
            rootPersistence,
            scanPersistence,
            hints,
            NullLogger<ImportedVideoMaterializer>.Instance);
        IImportedVideoMaterializer firstMaterializer = failMaterialization
            ? new FailOnCallImportedVideoMaterializer(realMaterializer, 1)
            : realMaterializer;
        var realMover = new ImportFileMover();
        IImportFileMover firstMover = failPlacementOnCall is { } failBeforeCall
            ? new FailOnCallImportFileMover(realMover, failBeforeCall, failAfterPlacement: false)
            : failAfterPlacementOnCall is { } failAfterCall
                ? new FailOnCallImportFileMover(realMover, failAfterCall, failAfterPlacement: true)
                : realMover;
        IOwnedFileReplacer firstReplacer = failCrossFormatAfterInstall
            ? new ThrowAfterCrossFormatInstallReplacer()
            : failSameFormatAfterStage
                ? new ThrowAfterReplacementStageReplacer()
                : failAfterReplacementEvidence
                    ? new FailAfterReplacementEvidenceReplacer()
                    : new OwnedFileReplacer(new MergedImportTestSupport.NoRecycleBin(), NullLogger<OwnedFileReplacer>.Instance);
        var resumeReplacer = new OwnedFileReplacer(
            new MergedImportTestSupport.NoRecycleBin(),
            NullLogger<OwnedFileReplacer>.Instance);
        var engine = CreateEngine(firstMaterializer, firstMover, firstReplacer);
        var resumeEngine = CreateEngine(realMaterializer, realMover, resumeReplacer);

        TvAcquisitionImportEngine CreateEngine(
            IImportedVideoMaterializer importedVideoMaterializer,
            IImportFileMover importFileMover,
            IOwnedFileReplacer ownedFileReplacer) => new(
            EntityKind.VideoSeason,
            store,
            new EfBookAcquisitionProfileStore(db),
            rootPersistence,
            new DownloadPayloadReader(),
            importFileMover,
            new ImportedTorrentRemover(store, new MergedImportTestSupport.ThrowingClientConfigStore(), new MergedImportTestSupport.ThrowingClientFactory(), NullLogger<ImportedTorrentRemover>.Instance),
            new EfImportTargetIndex(db),
            ownedFileReplacer,
            new EfAcquisitionBlocklistStore(db),
            history,
            importedVideoMaterializer,
            new VideoScanConcurrencyGate(),
            NullLogger<TvAcquisitionImportEngine>.Instance);

        var job = new JobRunSnapshot(
            jobId, JobType.AcquisitionImport, JobRunStatus.Running, 0, null, "{}",
            null, null, null, now, now, null);
        var queue = new MergedImportTestSupport.RecordingJobQueue();
        return new Harness(
            engine, resumeEngine, new JobContext(job, queue), import,
            libraryRoot, seriesFolder, seasonFolder, ownedEpisodePath, seasonId, ownedEpisodeId, wantedEpisodeId,
            queue);
    }

    private static Guid AddEntity(PrismediaDbContext db, string kindCode, Guid? parent, int? sortOrder, string sourcePath) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id, KindCode = kindCode, Title = kindCode, ParentEntityId = parent,
            SortOrder = sortOrder, CreatedAt = now, UpdatedAt = now
        });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(), EntityId = id, Role = EntityFileRole.Source, Path = sourcePath,
            CreatedAt = now, UpdatedAt = now
        });
        return id;
    }

    private static Guid AddWantedEntity(PrismediaDbContext db, string kindCode, Guid? parent, int? sortOrder) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kindCode,
            Title = kindCode,
            ParentEntityId = parent,
            SortOrder = sortOrder,
            IsWanted = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        return id;
    }

    private static async Task<AcquisitionStatus> StatusOf(PrismediaDbContext db, Guid acquisitionId) =>
        await db.Acquisitions.AsNoTracking().Where(row => row.Id == acquisitionId).Select(row => row.Status).SingleAsync();

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FailOnCallImportedVideoMaterializer(
        IImportedVideoMaterializer inner,
        int failingCall) : IImportedVideoMaterializer {
        private int _calls;

        public Task MaterializeAsync(
            JobContext context,
            ImportedTvMaterializationRequest request,
            CancellationToken cancellationToken) {
            _calls++;
            return _calls == failingCall
                ? throw new InvalidOperationException("Synthetic catalog failure.")
                : inner.MaterializeAsync(context, request, cancellationToken);
        }
    }

    private sealed class FailOnCallImportFileMover(
        IImportFileMover inner,
        int failingCall,
        bool failAfterPlacement) : IImportFileMover {
        private int _calls;

        public string ResolveExactTargetPath(
            string desiredTargetPath,
            IReadOnlyCollection<string> reservedTargetPaths) =>
            inner.ResolveExactTargetPath(desiredTargetPath, reservedTargetPaths);

        public Task<string> PlaceAsync(
            ResolvedImportItem item,
            ImportMode mode,
            CancellationToken cancellationToken) =>
            inner.PlaceAsync(item, mode, cancellationToken);

        public async Task<string> PlaceExactAsync(
            ResolvedImportItem item,
            ImportMode mode,
            CancellationToken cancellationToken) {
            _calls++;
            if (_calls == failingCall && !failAfterPlacement) {
                throw new IOException("Synthetic failure before exact placement.");
            }

            var finalPath = await inner.PlaceExactAsync(item, mode, cancellationToken);
            if (_calls == failingCall) {
                throw new IOException("Synthetic failure after exact placement.");
            }

            return finalPath;
        }
    }

    private sealed class ThrowAfterCrossFormatInstallReplacer : IOwnedFileReplacer {
        public Task<OwnedFileReplaceResult> ReplaceAsync(
            string ownedFolder,
            string newContentPath,
            BookFormatTier ownedFormatTier,
            CancellationToken cancellationToken,
            EntityKind kind = EntityKind.Book,
            bool allowFormatChange = false) {
            var target = Path.ChangeExtension(ownedFolder, Path.GetExtension(newContentPath));
            File.Move(newContentPath, target);
            throw new IOException("Synthetic process failure after installing the new extension.");
        }

        public Task<OwnedFileReplaceResult> ReplaceRetainingBackupAsync(
            string ownedFolder,
            string newContentPath,
            BookFormatTier ownedFormatTier,
            CancellationToken cancellationToken,
            EntityKind kind = EntityKind.Book,
            bool allowFormatChange = false,
            string? recoveryBackupPath = null,
            string? incomingEvidencePath = null) {
            Assert.NotNull(recoveryBackupPath);
            Assert.NotNull(incomingEvidencePath);
            File.Copy(newContentPath, incomingEvidencePath!);
            File.Copy(ownedFolder, recoveryBackupPath!);
            var target = Path.ChangeExtension(ownedFolder, Path.GetExtension(newContentPath));
            File.Move(newContentPath, target);
            throw new IOException("Synthetic process failure after installing the new extension.");
        }
    }

    private sealed class ThrowAfterReplacementStageReplacer : IOwnedFileReplacer {
        public Task<OwnedFileReplaceResult> ReplaceAsync(
            string ownedFolder,
            string newContentPath,
            BookFormatTier ownedFormatTier,
            CancellationToken cancellationToken,
            EntityKind kind = EntityKind.Book,
            bool allowFormatChange = false) {
            File.Move(newContentPath, OwnedFileReplacementArtifacts.StagedPath(ownedFolder));
            throw new IOException("Synthetic process failure after staging the replacement.");
        }
    }

    private sealed class FailAfterReplacementEvidenceReplacer : IOwnedFileReplacer {
        public Task<OwnedFileReplaceResult> ReplaceAsync(
            string ownedFolder,
            string newContentPath,
            BookFormatTier ownedFormatTier,
            CancellationToken cancellationToken,
            EntityKind kind = EntityKind.Book,
            bool allowFormatChange = false) =>
            Task.FromResult(OwnedFileReplaceResult.Failed("The durable overload was expected."));

        public Task<OwnedFileReplaceResult> ReplaceRetainingBackupAsync(
            string ownedFolder,
            string newContentPath,
            BookFormatTier ownedFormatTier,
            CancellationToken cancellationToken,
            EntityKind kind = EntityKind.Book,
            bool allowFormatChange = false,
            string? recoveryBackupPath = null,
            string? incomingEvidencePath = null) {
            Assert.NotNull(recoveryBackupPath);
            Assert.NotNull(incomingEvidencePath);
            var staged = OwnedFileReplacementArtifacts.StagedPath(ownedFolder);
            File.Move(newContentPath, staged);
            File.Copy(staged, incomingEvidencePath!);
            File.Copy(ownedFolder, recoveryBackupPath!);
            File.Delete(staged); // mirrors a caught install error that cleaned staging
            return Task.FromResult(OwnedFileReplaceResult.Failed("Synthetic install failure."));
        }
    }
}
