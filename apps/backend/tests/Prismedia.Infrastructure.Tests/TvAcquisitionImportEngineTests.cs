using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Jobs;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Serialization;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryDecodesPlacedNewEpisodeBeforeCatalogingIt(bool checkpointRecordedPlacement) {
        await using var db = CreateContext();
        const string file = "Show.S01E02.WEB-DL.1080p.mkv";
        var verifier = new TestVideoPayloadVerifier();
        var harness = await HarnessAsync(db, "Show.S01E01.WEB-DL.720p.mkv", [file], "Show S01 WEB-DL 1080p",
            failMaterialization: checkpointRecordedPlacement,
            failAfterPlacementOnCall: checkpointRecordedPlacement ? null : 1, videoVerifier: verifier);
        await Assert.ThrowsAnyAsync<Exception>(() => harness.Engine.ImportAsync(harness.Context, harness.Import, default));
        var resume = (await AcquisitionTestFactory.Store(db).GetImportContextAsync(harness.Import.Id, default))!;
        var unit = Assert.Single(resume.TvImportCheckpoint!.Units);
        Assert.Equal(checkpointRecordedPlacement, unit.FinalPath is not null);
        Assert.False(File.Exists(Path.Combine(harness.Import.ContentPath!, file)));
        Assert.True(File.Exists(unit.TargetAbsolutePath));
        verifier.Paths.Clear();
        verifier.Failure = "The downloaded video could not be decoded completely.";

        await harness.ResumeEngine.ImportAsync(harness.Context, resume, default);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Equal(unit.TargetAbsolutePath, Assert.Single(verifier.Paths));
        Assert.Equal("payload-bytes", await File.ReadAllTextAsync(unit.TargetAbsolutePath));
        Assert.Equal("owned-bytes", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        Assert.False(await db.EntityFiles.AnyAsync(row => row.EntityId == harness.WantedEpisodeId && row.Role == EntityFileRole.Source));
        Assert.NotNull((await AcquisitionTestFactory.Store(db).GetImportContextAsync(harness.Import.Id, default))!.TvImportCheckpoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptDecodePreservesPendingEpisodesEvenWhenManuallySelected(bool manual) {
        await using var db = CreateContext();
        const string file = "Show.S01E02.WEB-DL.1080p.mkv";
        var verifier = new TestVideoPayloadVerifier("The downloaded video could not be decoded completely.");
        var harness = await HarnessAsync(db, "Show.S01E01.WEB-DL.720p.mkv", [file], "Show S01 WEB-DL 1080p",
            videoVerifier: verifier);
        if (manual) {
            var store = AcquisitionTestFactory.Store(db);
            var selected = (await store.GetSelectedReleaseAsync(harness.Import.Id, default))!;
            await store.SetSelectedReleaseAsync(harness.Import.Id, selected with { ManualPick = true }, default);
        }

        await harness.Engine.ImportAsync(harness.Context, harness.Import, default);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Single(verifier.Paths);
        Assert.Equal("owned-bytes", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        Assert.Equal("payload-bytes", await File.ReadAllTextAsync(Path.Combine(harness.Import.ContentPath!, file)));
        Assert.False(await db.EntityFiles.AnyAsync(row => row.EntityId == harness.WantedEpisodeId && row.Role == EntityFileRole.Source));
    }

    [Theory]
    [InlineData(true, "unreadable", true)]
    [InlineData(false, "unreadable", true)]
    [InlineData(true, "invalid runtime", true)]
    [InlineData(false, "invalid runtime", true)]
    [InlineData(true, "lower resolution", true)]
    [InlineData(false, "lower resolution", true)]
    [InlineData(true, "profile changed", true)]
    [InlineData(false, "profile changed", true)]
    [InlineData(true, "wrong audio", true)]
    [InlineData(false, "wrong audio", true)]
    [InlineData(true, "verified", false)]
    [InlineData(false, "verified", false)]
    [InlineData(true, "manual quality", false)]
    [InlineData(false, "manual quality", false)]
    [InlineData(true, "manual unreadable", true)]
    [InlineData(false, "manual unreadable", true)]
    public async Task NewEpisodePlacementChecksActualVideoAndCurrentProfile(bool merged, string scenario, bool held) {
        await using var db = CreateContext();
        var video = new VideoProbeData(1200, 1000, 1920, 1080, 24, null, null, null, null, null, null,
            [new(0, StreamKind.Audio.ToCode(), null, "eng", null, null, null, null, null, null, null, false, false)]);
        var probe = new NewEpisodeProbe(scenario switch {
            "unreadable" or "manual unreadable" => null,
            "invalid runtime" => video with { DurationSeconds = double.NaN },
            "lower resolution" => video with { Width = 1280, Height = 720 },
            "wrong audio" => video with { Streams = [new(0, StreamKind.Audio.ToCode(), null, "jpn", null, null, null, null, null, null, null, false, false)] },
            _ => video
        });
        const string fileName = "Show.S01E02.WEB-DL.1080p.mkv";
        var harness = await HarnessAsync(db, "Show.S01E01.WEB-DL.720p.mkv", [fileName], "Show S01 WEB-DL 1080p",
            linkEntity: merged, mediaProbe: probe);
        db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow {
            Id = Guid.NewGuid(), Kind = AcquisitionProfileKinds.For(EntityKind.VideoSeason), DisplayName = "Validation profile", IsDefault = true,
            AllowedQualities = [scenario is "profile changed" or "manual quality" ? VideoQuality.Webdl2160p.ToCode() : VideoQuality.Webdl1080p.ToCode()],
            PreferredLanguages = ["en"]
        });
        if (scenario.StartsWith("manual")) {
            var store = AcquisitionTestFactory.Store(db);
            var release = (await store.GetSelectedReleaseAsync(harness.Import.Id, default))!;
            await store.SetSelectedReleaseAsync(harness.Import.Id, release with { ManualPick = true }, default);
        }
        await db.SaveChangesAsync();

        await harness.Engine.ImportAsync(harness.Context, harness.Import, default);

        var acquisition = await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == harness.Import.Id);
        Assert.Equal(held ? AcquisitionStatus.ManualImportRequired : AcquisitionStatus.Importing, acquisition.Status);
        Assert.Single(probe.Paths);
        Assert.Equal("owned-bytes", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        if (held) {
            Assert.Equal("payload-bytes", await File.ReadAllTextAsync(Path.Combine(harness.Import.ContentPath!, fileName)));
            Assert.False(await db.EntityFiles.AnyAsync(row => row.EntityId == harness.WantedEpisodeId && row.Role == EntityFileRole.Source));
        }
    }

    [Fact]
    public async Task InvalidLaterEpisodeHoldsTheWholePendingPackBeforeAnyPlacement() {
        await using var db = CreateContext();
        var video = new VideoProbeData(1200, 1000, 1920, 1080, 24, null, null, null, null, null, null);
        var probe = new NewEpisodeProbe(video) {
            Resolve = path => path.Contains("E03") ? video with { Width = 1280, Height = 720 } : video
        };
        string[] files = ["Show.S01E02.WEB-DL.1080p.mkv", "Show.S01E03.WEB-DL.1080p.mkv"];
        var harness = await HarnessAsync(db, "Show.S01E01.WEB-DL.720p.mkv", files, "Show S01 WEB-DL 1080p",
            wantedEpisodeNumbers: [2, 3], mediaProbe: probe);

        await harness.Engine.ImportAsync(harness.Context, harness.Import, default);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Equal(2, probe.Paths.Count);
        foreach (var file in files) Assert.Equal("payload-bytes", await File.ReadAllTextAsync(Path.Combine(harness.Import.ContentPath!, file)));
        Assert.Single(await db.EntityFiles.Where(row => row.Role == EntityFileRole.Source).ToArrayAsync());
        Assert.All((await AcquisitionTestFactory.Store(db).GetImportContextAsync(harness.Import.Id, default))!.TvImportCheckpoint!.Units,
            unit => Assert.Null(unit.FinalPath));
    }

    [Fact]
    public async Task PendingNewEpisodeRechecksProfileAndResumesWithoutRedownload() {
        await using var db = CreateContext();
        var probe = new NewEpisodeProbe(new(1200, 1000, 1920, 1080, 24, null, null, null, null, null, null));
        const string file = "Show.S01E02.WEB-DL.1080p.mkv";
        var harness = await HarnessAsync(db, "Show.S01E01.WEB-DL.720p.mkv", [file], "Show S01 WEB-DL 2160p",
            failPlacementOnCall: 1, mediaProbe: probe);
        await Assert.ThrowsAsync<IOException>(() => harness.Engine.ImportAsync(harness.Context, harness.Import, default));
        var store = AcquisitionTestFactory.Store(db);
        var resume = (await store.GetImportContextAsync(harness.Import.Id, default))!;
        var profile = new BookAcquisitionProfileRow { Id = Guid.NewGuid(),
            Kind = AcquisitionProfileKinds.For(EntityKind.VideoSeason), DisplayName = "Current profile", IsDefault = true,
            AllowedQualities = [VideoQuality.Webdl2160p.ToCode()] };
        db.BookAcquisitionProfiles.Add(profile);
        await db.SaveChangesAsync();

        await harness.ResumeEngine.ImportAsync(harness.Context, resume, default);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Equal("payload-bytes", await File.ReadAllTextAsync(Path.Combine(harness.Import.ContentPath!, file)));
        Assert.Equal(2, probe.Paths.Count);
        profile.AllowedQualities = [VideoQuality.Webdl1080p.ToCode()];
        await db.SaveChangesAsync();
        var heldImport = (await store.GetImportContextAsync(harness.Import.Id, default))!;
        Assert.True(await store.TryClaimTvImportCheckpointAsync(harness.Import.Id, heldImport.TvImportCheckpoint!, harness.Context.Job.Id, default));
        await harness.ResumeEngine.ImportAsync(harness.Context, (await store.GetImportContextAsync(harness.Import.Id, default))!, default);

        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
        Assert.Equal(3, probe.Paths.Count);
        Assert.Equal("payload-bytes", await File.ReadAllTextAsync((await db.EntityFiles.SingleAsync(row => row.EntityId == harness.WantedEpisodeId)).Path));
        Assert.Equal(resume.TvImportCheckpoint!.AttemptId,
            (await store.GetImportContextAsync(harness.Import.Id, default))!.TvImportCheckpoint!.AttemptId);
    }

    [Fact]
    public async Task AlreadyPlacedEpisodeFinishesRecoveryWithoutReprobingTheConsumedPayload() {
        await using var db = CreateContext();
        var probe = new NewEpisodeProbe(new(1200, 1000, 1920, 1080, 24, null, null, null, null, null, null));
        var harness = await HarnessAsync(db, "Show.S01E01.WEB-DL.720p.mkv", ["Show.S01E02.WEB-DL.1080p.mkv"], "Show S01 WEB-DL 1080p",
            failMaterialization: true, mediaProbe: probe);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Engine.ImportAsync(harness.Context, harness.Import, default));
        Assert.Single(probe.Paths);
        var store = AcquisitionTestFactory.Store(db);
        var resume = (await store.GetImportContextAsync(harness.Import.Id, default))!;
        probe.Resolve = _ => throw new InvalidOperationException("Placed files must not be treated as pending payloads");

        await harness.ResumeEngine.ImportAsync(harness.Context, resume, default);

        Assert.Single(probe.Paths);
        Assert.Equal("payload-bytes", await File.ReadAllTextAsync((await db.EntityFiles.SingleAsync(row => row.EntityId == harness.WantedEpisodeId)).Path));
    }

    [Fact]
    public async Task NewMonitoredExtraUsesItsOwnSeasonsProfile() {
        await using var db = CreateContext();
        var probe = new NewEpisodeProbe(new(1200, 1000, 1920, 1080, 24, null, null, null, null, null, null));
        const string extra = "Show.S02E03.Hidden.Garden.WEB-DL.1080p.mkv";
        var harness = await HarnessAsync(db, "Show.S01E01.WEB-DL.720p.mkv", ["Show.S01E02.WEB-DL.1080p.mkv", extra], "Show S01 WEB-DL 1080p", mediaProbe: probe);
        var season = AddWantedEntity(db, EntityKind.VideoSeason.ToCode(), harness.SeriesId, 2);
        var episode = AddWantedEntity(db, EntityKind.VideoEpisode.ToCode(), season, 3);
        db.Entities.Local.Single(row => row.Id == episode).Title = "Hidden Garden";
        var profile = new BookAcquisitionProfileRow { Id = Guid.NewGuid(),
            Kind = AcquisitionProfileKinds.For(EntityKind.VideoSeason), DisplayName = "Extra season profile", IsDefault = false,
            AllowedQualities = [VideoQuality.Webdl2160p.ToCode()] };
        db.BookAcquisitionProfiles.Add(profile);
        await db.SaveChangesAsync();
        var monitor = await new EfMonitorStore(db).StartForEntityAsync(season, EntityKind.VideoSeason, "Season 2", null, null, default);
        (await db.Monitors.SingleAsync(row => row.Id == monitor.Id)).ProfileId = profile.Id;
        await db.SaveChangesAsync();

        await harness.Engine.ImportAsync(harness.Context, harness.Import, default);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.True(File.Exists(Path.Combine(harness.Import.ContentPath!, extra)));
        Assert.False(await db.EntityFiles.AnyAsync(row => row.EntityId == episode && row.Role == EntityFileRole.Source));
        profile.AllowedQualities = [VideoQuality.Webdl1080p.ToCode()];
        await db.SaveChangesAsync();
        var store = AcquisitionTestFactory.Store(db);
        var heldImport = (await store.GetImportContextAsync(harness.Import.Id, default))!;
        Assert.True(await store.TryClaimTvImportCheckpointAsync(harness.Import.Id, heldImport.TvImportCheckpoint!, harness.Context.Job.Id, default));
        await harness.ResumeEngine.ImportAsync(harness.Context, (await store.GetImportContextAsync(harness.Import.Id, default))!, default);
        Assert.True(await db.EntityFiles.AnyAsync(row => row.EntityId == episode && row.Role == EntityFileRole.Source));
    }

    [Theory]
    [InlineData(true, false, 2)]
    [InlineData(false, false, 2)]
    [InlineData(true, true, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, null, 2)]
    [InlineData(false, null, 2)]
    [InlineData(true, false, 0)]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, null, 0)]
    [InlineData(false, null, 0)]
    public async Task IdentifiedForeignSeasonEpisodesImportOnlyWhenThatSeasonIsMonitored(bool monitored, bool? mislabeled, int foreignSeason) {
        await using var db = CreateContext();
        var extraName = mislabeled switch {
            null => "Show - Hidden Garden & Mountain Journey.mkv",
            true => "Show.S01E49-E50.Hidden.Garden.&.Mountain.Journey.mkv",
            false => $"Show.S{foreignSeason:00}E03-E04.Hidden.Garden.&.Mountain.Journey.mkv"
        };
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01.mkv",
            payloadFiles: ["Show.S01E02.mkv", extraName], releaseTitle: "Show S01");
        var otherSeason = AddWantedEntity(db, EntityKind.VideoSeason.ToCode(), harness.SeriesId, foreignSeason);
        var first = AddWantedEntity(db, EntityKind.VideoEpisode.ToCode(), otherSeason, 3);
        var second = AddWantedEntity(db, EntityKind.VideoEpisode.ToCode(), otherSeason, 4);
        db.Entities.Local.Single(row => row.Id == first).Title = "Hidden Garden";
        db.Entities.Local.Single(row => row.Id == second).Title = "Mountain Journey";
        if (mislabeled is null) {
            (await db.Entities.SingleAsync(row => row.ParentEntityId == harness.SeasonId && row.SortOrder == 1)).Title = "Hidden Garden";
        }
        await db.SaveChangesAsync();
        if (monitored) {
            await new EfMonitorStore(db).StartForEntityAsync(otherSeason, EntityKind.VideoSeason, $"Show Season {foreignSeason}",
                targeting: null, preset: null, cancellationToken: default);
        }

        await harness.Engine.ImportAsync(harness.Context, harness.Import, default);

        await AcquisitionTestFactory.Store(db).MarkImportedWithQualityAsync(harness.Import.Id, BookQualityRank.Floor, "Imported", default);
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == harness.WantedEpisodeId)).IsWanted);
        Assert.False(await db.Entities.AnyAsync(row => row.ParentEntityId == harness.SeasonId && row.SortOrder == 49));
        var foreignSources = await db.EntityFiles.AsNoTracking().Where(row => (row.EntityId == first || row.EntityId == second)
            && row.Role == EntityFileRole.Source).ToArrayAsync();
        Assert.Equal(monitored ? 2 : 0, foreignSources.Length);
        Assert.Equal(!monitored, File.Exists(Path.Combine(harness.Import.ContentPath!, extraName)));
        Assert.Equal(monitored ? AcquisitionStatus.Imported : AcquisitionStatus.ManualImportRequired,
            await StatusOf(db, harness.Import.Id));
        if (monitored) {
            Assert.Single(foreignSources.Select(source => source.Path).Distinct());
            Assert.All(foreignSources, source => Assert.Contains($"Season {foreignSeason:00}", source.Path));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignSeasonMonitorPauseIsResolvedBeforePlacementAndDurablePlansSurviveRetry(bool pauseAfterElection) {
        await using var db = CreateContext();
        var extraName = "Show.S02E03.Hidden.Garden.mkv";
        Guid? monitorId = null;
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01.mkv",
            payloadFiles: ["Show.S01E02.mkv", extraName], releaseTitle: "Show S01",
            failPlacementOnCall: pauseAfterElection ? 1 : null,
            beforeCheckpoint: pauseAfterElection ? null : () => {
                db.Monitors.Single(row => row.Id == monitorId).Status = MonitorStatus.Paused;
                db.SaveChanges();
            });
        var season = AddWantedEntity(db, EntityKind.VideoSeason.ToCode(), harness.SeriesId, 2);
        var episode = AddWantedEntity(db, EntityKind.VideoEpisode.ToCode(), season, 3);
        db.Entities.Local.Single(row => row.Id == episode).Title = "Hidden Garden";
        await db.SaveChangesAsync();
        monitorId = (await new EfMonitorStore(db).StartForEntityAsync(season, EntityKind.VideoSeason, "Show Season 2",
            null, null, default)).Id;
        var store = AcquisitionTestFactory.Store(db);
        if (pauseAfterElection) {
            await Assert.ThrowsAsync<IOException>(() => harness.Engine.ImportAsync(harness.Context, harness.Import, default));
            Assert.NotNull((await db.Acquisitions.AsNoTracking().SingleAsync()).ImportCheckpointJson);
            db.Monitors.Single(row => row.Id == monitorId).Status = MonitorStatus.Paused;
            await db.SaveChangesAsync();
            await harness.ResumeEngine.ImportAsync(harness.Context, (await store.GetImportContextAsync(harness.Import.Id, default))!, default);
            await store.MarkImportedWithQualityAsync(harness.Import.Id, BookQualityRank.Floor, "Imported", default);
            Assert.True(await db.EntityFiles.AnyAsync(row => row.EntityId == episode && row.Role == EntityFileRole.Source));
        } else {
            await harness.Engine.ImportAsync(harness.Context, harness.Import, default);
            Assert.Null((await db.Acquisitions.AsNoTracking().SingleAsync()).ImportCheckpointJson);
            Assert.False(await db.EntityFiles.AnyAsync(row => row.EntityId == episode && row.Role == EntityFileRole.Source));
            Assert.True(File.Exists(Path.Combine(harness.Import.ContentPath!, extraName)));
            Assert.True(File.Exists(Path.Combine(harness.Import.ContentPath!, "Show.S01E02.mkv")));
        }
    }

    [Fact]
    public async Task ExcludingForeignFilesCannotTurnAnUnidentifiedFileIntoASingleEpisodeGuess() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01.mkv",
            payloadFiles: ["unidentified.mkv", "Show.S02E03.mkv"], releaseTitle: "Show S01E02");

        await harness.Engine.ImportAsync(harness.Context, harness.Import with { EpisodeNumber = 2 }, default);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.True(File.Exists(Path.Combine(harness.Import.ContentPath!, "unidentified.mkv")));
        Assert.False(await db.EntityFiles.AnyAsync(row => row.EntityId == harness.WantedEpisodeId && row.Role == EntityFileRole.Source));
    }

    [Fact]
    public async Task OwnedForeignEpisodeIsRetainedInsteadOfUpgradedUnderTheRequestedSeasonsProfile() {
        await using var db = CreateContext();
        const string extraName = "Show.S02E03.Hidden.Garden.2160p.WEB-DL.mkv";
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01.mkv",
            payloadFiles: ["Show.S01E02.mkv", extraName], releaseTitle: "Show S01 2160p WEB-DL");
        var folder = Directory.CreateDirectory(Path.Combine(harness.SeriesFolder, "Season 02")).FullName;
        var owned = Path.Combine(folder, "Show.S02E03.1080p.WEB-DL.mkv");
        await File.WriteAllTextAsync(owned, "original foreign episode");
        var season = AddFolderEntity(db, EntityKind.VideoSeason.ToCode(), harness.SeriesId, 2, folder);
        var episode = AddEntity(db, EntityKind.VideoEpisode.ToCode(), season, 3, owned);
        db.Entities.Local.Single(row => row.Id == episode).Title = "Hidden Garden";
        await db.SaveChangesAsync();
        await new EfMonitorStore(db).StartForEntityAsync(season, EntityKind.VideoSeason, "Show Season 2", null, null, default);
        var originalSource = await db.EntityFiles.AsNoTracking().SingleAsync(row => row.EntityId == episode && row.Role == EntityFileRole.Source);

        await harness.Engine.ImportAsync(harness.Context, harness.Import, default);
        await AcquisitionTestFactory.Store(db).MarkImportedWithQualityAsync(harness.Import.Id, BookQualityRank.Floor, "Imported", default);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Equal("original foreign episode", await File.ReadAllTextAsync(owned));
        Assert.True(File.Exists(Path.Combine(harness.Import.ContentPath!, extraName)));
        Assert.Equal(originalSource.Id, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.EntityId == episode
            && row.Role == EntityFileRole.Source)).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlacedFileRecoveryRestoresBothEpisodesFromACombinedFile(bool renamedWithLedger) {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01.mkv", payloadFiles: [],
            releaseTitle: "Show S01", wantedEpisodeNumbers: [2, 3]);
        var sourceName = "Show.S01E02-E03.mkv";
        var placed = Path.Combine(harness.SeasonFolder, renamedWithLedger ? "Show - S01E02.mkv" : sourceName);
        await File.WriteAllTextAsync(placed, "paired episode payload");
        var acquisition = await db.Acquisitions.SingleAsync(row => row.Id == harness.Import.Id);
        acquisition.FinalSourcePath = harness.SeasonFolder;
        if (renamedWithLedger) {
            acquisition.ImportResultJson = JsonSerializer.Serialize(new AcquisitionImportFileLedger(AcquisitionImportPhase.Importing, [
                new("paired", sourceName, new FileInfo(placed).Length, sourceName, Path.GetRelativePath(harness.LibraryRoot, placed),
                    AcquisitionImportFileRole.Media, AcquisitionImportContentKind.Video, AcquisitionImportFileStatus.Imported,
                    AcquisitionImportDecision.PlaceNew, null)
            ]), new JsonSerializerOptions { Converters = { new CodecJsonConverterFactory() } });
        }
        await db.SaveChangesAsync();

        await harness.Engine.ImportAsync(harness.Context, harness.Import with { FinalSourcePath = harness.SeasonFolder }, default);

        var episodes = await db.Entities.AsNoTracking().Where(row => row.ParentEntityId == harness.SeasonId
            && (row.SortOrder == 2 || row.SortOrder == 3)).OrderBy(row => row.SortOrder).ToArrayAsync();
        Assert.Equal(2, episodes.Length);
        Assert.All(episodes, episode => Assert.False(episode.IsWanted));
        foreach (var episode in episodes) {
            Assert.Equal(placed, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.EntityId == episode.Id
                && row.Role == EntityFileRole.Source)).Path);
        }
        Assert.Equal("paired episode payload", await File.ReadAllTextAsync(placed));
    }

    [Fact]
    public async Task LegacyPlacedFileRecoveryDecodesBeforeClearingWantedEpisodes() {
        await using var db = CreateContext();
        var verifier = new TestVideoPayloadVerifier("The downloaded video could not be decoded completely.");
        var harness = await HarnessAsync(db, "Show - s01e01.mkv", [], "Show S01", videoVerifier: verifier);
        var placed = Path.Combine(harness.SeasonFolder, "Show.S01E02.mkv");
        await File.WriteAllTextAsync(placed, "damaged episode payload");
        (await db.Acquisitions.SingleAsync()).FinalSourcePath = placed;
        await db.SaveChangesAsync();

        await harness.Engine.ImportAsync(harness.Context, harness.Import with { FinalSourcePath = placed }, default);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Equal(placed, Assert.Single(verifier.Paths));
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == harness.WantedEpisodeId)).IsWanted);
        Assert.False(await db.EntityFiles.AnyAsync(row => row.EntityId == harness.WantedEpisodeId && row.Role == EntityFileRole.Source));
        Assert.Equal("damaged episode payload", await File.ReadAllTextAsync(placed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlacedFileRecoveryPreservesIncompleteMappingEvidenceForReview(bool unnumberedWanted) {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01.mkv", payloadFiles: [],
            releaseTitle: "Show S01", wantedEpisodeNumbers: [2, 3]);
        var placed = Path.Combine(harness.SeasonFolder, "Show.S01E02-E03.mkv");
        await File.WriteAllTextAsync(placed, "paired episode payload");
        if (unnumberedWanted) {
            (await db.Entities.SingleAsync(row => row.Id == harness.WantedEpisodeId)).SortOrder = null;
        } else {
            await File.WriteAllTextAsync(Path.Combine(harness.SeasonFolder, "unidentified.mkv"), "unidentified video");
        }
        (await db.Acquisitions.SingleAsync()).FinalSourcePath = harness.SeasonFolder;
        await db.SaveChangesAsync();

        await harness.Engine.ImportAsync(harness.Context, harness.Import with { FinalSourcePath = harness.SeasonFolder }, default);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.True((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == harness.WantedEpisodeId)).IsWanted);
        Assert.False(await db.EntityFiles.AnyAsync(row => row.EntityId == harness.WantedEpisodeId && row.Role == EntityFileRole.Source));
        Assert.Equal("paired episode payload", await File.ReadAllTextAsync(placed));
        Assert.True(File.Exists(harness.OwnedEpisodePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnnumberedWantedEpisodesHoldAutomaticImportWithoutMovingFiles(bool existingLayout) {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01.mkv",
            payloadFiles: ["Show.S01E02-E03.1080p.WEB-DL.mkv"], releaseTitle: "Show S01 1080p WEB-DL");
        var wanted = await db.Entities.SingleAsync(row => row.Id == harness.WantedEpisodeId);
        wanted.SortOrder = null;
        if (!existingLayout) {
            db.EntitySources.RemoveRange(await db.EntitySources
                .Where(row => row.EntityId == harness.SeriesId || row.EntityId == harness.SeasonId).ToArrayAsync());
        }
        await db.SaveChangesAsync();
        var entityCount = await db.Entities.CountAsync();

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.True(File.Exists(Path.Combine(harness.Import.ContentPath!, "Show.S01E02-E03.1080p.WEB-DL.mkv")));
        Assert.Equal(entityCount, await db.Entities.CountAsync());
        Assert.False(await db.EntityFiles.AnyAsync(row => row.EntityId == wanted.Id && row.Role == EntityFileRole.Source));
        Assert.Null((await db.Acquisitions.SingleAsync()).ImportCheckpointJson);
    }

    public void Dispose() {
        try {
            Directory.Delete(_workRoot, recursive: true);
        } catch {
            // best-effort temp cleanup
        }
    }

    [Theory]
    [InlineData(true, "Show.S02E01.mkv")]
    [InlineData(false, "Show.S02E01.mkv")]
    [InlineData(true, "unidentified.mkv")]
    [InlineData(false, "unidentified.mkv")]
    public async Task AutomaticSeasonImportRetainsUnmappedExtraVideosForReview(bool existingLayout, string extraName) {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01.mkv",
            payloadFiles: ["Show.S01E02.mkv", extraName], releaseTitle: "Show S01");
        if (!existingLayout) {
            db.EntitySources.RemoveRange(await db.EntitySources
                .Where(row => row.EntityId == harness.SeriesId || row.EntityId == harness.SeasonId).ToArrayAsync());
            foreach (var entity in await db.Entities.Where(row => row.Id == harness.SeriesId || row.Id == harness.SeasonId).ToArrayAsync()) {
                entity.IsWanted = true;
            }
            await db.SaveChangesAsync();
        }

        await harness.Engine.ImportAsync(harness.Context, harness.Import, default);

        var store = AcquisitionTestFactory.Store(db);
        await store.MarkImportedWithQualityAsync(harness.Import.Id, BookQualityRank.Floor, "Imported", default);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Equal("payload-bytes", await File.ReadAllTextAsync(Path.Combine(harness.Import.ContentPath!, extraName)));
        Assert.False(File.Exists(Path.Combine(harness.Import.ContentPath!, "Show.S01E02.mkv")));
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == harness.WantedEpisodeId)).IsWanted);
        var ledger = (await store.GetTransferInfoAsync(harness.Import.Id, default))!.ImportResult!;
        Assert.True(ledger.HasRetainedTvVideos());
        Assert.Contains(ledger.Files, file => file.SourceRelativePath == extraName
            && file.Status == AcquisitionImportFileStatus.Skipped && file.DestinationRelativePath is null);
        Assert.Null((await db.Acquisitions.SingleAsync()).ImportCheckpointJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitMappingFinishesRetainedPayloadWithoutReplayingTheAlreadyImportedFile(bool paired) {
        await using var db = CreateContext();
        var originalName = paired ? "Show.S01E02-E03.mkv" : "Show.S01E02.mkv";
        var finalEpisode = paired ? 4 : 3;
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01.mkv",
            payloadFiles: [originalName, "unidentified.mkv"], releaseTitle: "Show S01",
            wantedEpisodeNumbers: paired ? [2, 3, 4] : [2, 3]);
        var store = AcquisitionTestFactory.Store(db);
        await harness.Engine.ImportAsync(harness.Context, harness.Import, default);
        await store.MarkImportedWithQualityAsync(harness.Import.Id, BookQualityRank.Floor, "Imported", default);
        var originalSource = await db.EntityFiles.AsNoTracking().SingleAsync(row => row.EntityId == harness.WantedEpisodeId
            && row.Role == EntityFileRole.Source);
        var third = await db.Entities.SingleAsync(row => row.ParentEntityId == harness.SeasonId && row.SortOrder == finalEpisode);
        var acquisition = await db.Acquisitions.SingleAsync();
        acquisition.Status = AcquisitionStatus.Importing;
        acquisition.ImportClaimJobId = harness.Context.Job.Id;
        await db.SaveChangesAsync();
        var retry = (await store.GetImportContextAsync(harness.Import.Id, default))! with {
            ManualFileMappings = [new ManualImportFileMapping("unidentified.mkv", third.Id, 1, finalEpisode)]
        };

        await harness.ResumeEngine.ImportAsync(harness.Context, retry, default);
        await store.MarkImportedWithQualityAsync(harness.Import.Id, BookQualityRank.Floor, "Imported", default);

        Assert.Equal(AcquisitionStatus.Imported, await StatusOf(db, harness.Import.Id));
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == third.Id)).IsWanted);
        Assert.Equal("payload-bytes", await File.ReadAllTextAsync(originalSource.Path));
        Assert.Equal(originalSource.Id, (await db.EntityFiles.AsNoTracking().SingleAsync(row => row.EntityId == harness.WantedEpisodeId
            && row.Role == EntityFileRole.Source)).Id);
        Assert.False(File.Exists(Path.Combine(harness.Import.ContentPath!, "unidentified.mkv")));
        var completedLedger = (await store.GetTransferInfoAsync(harness.Import.Id, default))!.ImportResult!;
        Assert.False(completedLedger.HasRetainedTvVideos());
        Assert.Equal(2, completedLedger.Files.Count);
        Assert.Contains(completedLedger.Files, file => file.SourceRelativePath == originalName);
        Assert.All(completedLedger.Files, file => {
            Assert.Equal(AcquisitionImportFileStatus.Imported, file.Status);
            Assert.Equal("payload-bytes".Length, file.SizeBytes);
        });
    }

    [Fact]
    public async Task LegacyCheckpointRecoversOmittedExtraVideosBeforeFinishingTheImport() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01.mkv",
            payloadFiles: ["Show.S01E02.mkv", "Show.S02E01.mkv"], releaseTitle: "Show S01", failMaterialization: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Engine.ImportAsync(harness.Context, harness.Import, default));
        var store = AcquisitionTestFactory.Store(db);
        var checkpoint = (await store.GetImportContextAsync(harness.Import.Id, default))!.TvImportCheckpoint!;
        var legacy = checkpoint with {
            ImportFileLedger = checkpoint.ImportFileLedger! with {
                Files = checkpoint.ImportFileLedger.Files.Where(file => file.SourceRelativePath != "Show.S02E01.mkv").ToArray()
            },
            DiscardRemainingPayload = true
        };
        await store.SetTvImportCheckpointAsync(harness.Import.Id, legacy, default);

        await harness.ResumeEngine.ImportAsync(harness.Context, (await store.GetImportContextAsync(harness.Import.Id, default))!, default);
        await store.MarkImportedWithQualityAsync(harness.Import.Id, BookQualityRank.Floor, "Imported", default);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.False((await db.Entities.AsNoTracking().SingleAsync(row => row.Id == harness.WantedEpisodeId)).IsWanted);
        Assert.True((await store.GetTransferInfoAsync(harness.Import.Id, default))!.ImportResult!.HasRetainedTvVideos());
        Assert.Equal("payload-bytes", await File.ReadAllTextAsync(Path.Combine(harness.Import.ContentPath!, "Show.S02E01.mkv")));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task RetainedIdenticalPairedFileRepairsMissingEpisodeOwnerWithoutReplacingBytes(bool identical, bool includeNewEpisode) {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db,
            ownedEpisodeName: "Show - S01E01-E02 1080p WEB-DL.mkv",
            payloadFiles: includeNewEpisode
                ? ["Show.S01E01-E02.1080p.WEB-DL.mkv", "Show.S01E03.1080p.WEB-DL.mkv"]
                : ["Show.S01E01-E02.1080p.WEB-DL.mkv"],
            releaseTitle: "Show S01 1080p WEB-DL",
            payloadContent: identical ? "owned-bytes" : "other-bytes",
            wantedEpisodeNumbers: includeNewEpisode ? [2, 3] : [2]);

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal("owned-bytes", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        if (!identical) {
            Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
            Assert.True((await db.Entities.FindAsync(harness.WantedEpisodeId))!.IsWanted);
            return;
        }
        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
        Assert.False((await db.Entities.FindAsync(harness.WantedEpisodeId))!.IsWanted);
        Assert.Equal(harness.OwnedEpisodePath, (await db.EntityFiles.SingleAsync(file =>
            file.EntityId == harness.WantedEpisodeId && file.Role == EntityFileRole.Source)).Path);
        Assert.Equal(includeNewEpisode ? 2 : 1, Directory.GetFiles(harness.SeasonFolder, "*.mkv").Length);
        Assert.Empty(await db.AcquisitionBlocklist.ToArrayAsync());
        var checkpoint = (await AcquisitionTestFactory.Store(db).GetImportContextAsync(harness.Import.Id, CancellationToken.None))!.TvImportCheckpoint!;
        Assert.True(checkpoint.Units.Single(unit => unit.EpisodeNumber == 1).AdoptedExistingTarget);
        if (includeNewEpisode) {
            Assert.False(checkpoint.Units.Single(unit => unit.EpisodeNumber == 3).AdoptedExistingTarget);
        }
    }

    [Fact]
    public async Task IdenticalBytesDoNotCollapseTwoOwnedFilesIntoOnePairedFile() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, "Show - S01E01 1080p WEB-DL.mkv",
            ["Show.S01E01-E02.1080p.WEB-DL.mkv"], "Show S01 1080p WEB-DL", payloadContent: "owned-bytes");
        var secondPath = Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv");
        await File.WriteAllTextAsync(secondPath, "owned-bytes");
        var episode = await db.Entities.FindAsync(harness.WantedEpisodeId);
        episode!.IsWanted = false;
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(), EntityId = episode.Id, Role = EntityFileRole.Source, Path = secondPath
        });
        await db.SaveChangesAsync();

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Equal(secondPath, (await db.EntityFiles.SingleAsync(file => file.EntityId == episode.Id)).Path);
        Assert.Equal(2, Directory.GetFiles(harness.SeasonFolder, "*.mkv").Length);
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
        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
        Assert.Empty(await db.AcquisitionBlocklist.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task ReviewedMappingImportsATokenlessFileIntoTheChosenEpisode() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["mystery-video.mkv"],
            releaseTitle: "Show Season 1 1080p WEB-DL");

        await harness.Engine.ImportAsync(
            harness.Context,
            harness.Import with {
                ManualFileMappings = [
                    new ManualImportFileMapping("mystery-video.mkv", harness.WantedEpisodeId, 1, 2)
                ]
            },
            CancellationToken.None);

        var target = Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv");
        Assert.True(File.Exists(target));
        var source = await db.EntityFiles.AsNoTracking().SingleAsync(row =>
            row.EntityId == harness.WantedEpisodeId && row.Role == EntityFileRole.Source);
        Assert.Equal(target, source.Path);
        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
    }

    [Fact]
    public async Task EpisodePayloadForAnotherUnitReportsAnIdentifierMismatch() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E03.Other.Episode.mkv"],
            releaseTitle: "Show S01E03 1080p WEB-DL");

        await harness.Engine.ImportAsync(
            harness.Context,
            harness.Import with { Kind = EntityKind.VideoEpisode, EpisodeNumber = 2 },
            CancellationToken.None);

        var acquisition = await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == harness.Import.Id);
        Assert.Equal(AcquisitionStatus.ManualImportRequired, acquisition.Status);
        Assert.Contains("identifiers do not agree", acquisition.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteSeriesPayloadImportsRequestedSeasonAndRetainsOtherSeasonForReview() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv", "Show.S02E01.1080p.WEB-DL.mkv"],
            releaseTitle: "Show Complete Series 1080p WEB-DL");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv")));
        Assert.False(File.Exists(Path.Combine(harness.SeriesFolder, "Season 02", "Show - S02E01.mkv")));
        var checkpoint = await AcquisitionTestFactory.Store(db)
            .GetImportContextAsync(harness.Import.Id, CancellationToken.None);
        Assert.False(checkpoint!.TvImportCheckpoint!.DiscardRemainingPayload);
        Assert.True(checkpoint.TvImportCheckpoint.ImportFileLedger!.HasRetainedTvVideos());
        Assert.True(File.Exists(Path.Combine(harness.Import.ContentPath!, "Show.S02E01.1080p.WEB-DL.mkv")));
    }

    [Fact]
    public async Task PartialSeasonImportQueuesImmediateMissingEpisodeFallback() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"],
            releaseTitle: "Show S01 1080p WEB-DL",
            wantedEpisodeNumbers: [2, 3],
            enableMissingFallback: true);

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Contains(harness.Queue.Enqueued, request =>
            request.Type == JobType.MonitoredSearch
            && request.TargetLabel == "Fill missing imported episodes");
    }

    [Fact]
    public async Task ImportedMeansWantedEpisodeIsImmediatelySourceBackedWithoutRunningAScanJob() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"], releaseTitle: "Show S01 1080p WEB-DL");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        var wantedEpisode = await db.Entities.AsNoTracking().SingleAsync(row => row.Id == harness.WantedEpisodeId);
        var source = await db.EntityFiles.AsNoTracking()
            .SingleAsync(row => row.EntityId == harness.WantedEpisodeId && row.Role == EntityFileRole.Source);

        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
        Assert.False(wantedEpisode.IsWanted);
        Assert.Equal(Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv"), source.Path);
        Assert.Single(await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == harness.SeasonId && row.KindCode == EntityKind.VideoEpisode.ToCode() && row.SortOrder == 2)
            .ToArrayAsync());
        Assert.DoesNotContain(harness.Queue.Enqueued, request => request.Type == JobType.ScanLibrary);

        Assert.DoesNotContain(harness.Queue.Enqueued, request => request.Type == JobType.ExtractSubtitles);
        var reconciliation = Assert.Single(harness.Queue.Enqueued, request => request.Type == JobType.ReconcileEntity);
        Assert.Equal(EntityKind.VideoSeries.ToCode(), reconciliation.TargetEntityKind);
        Assert.Equal(harness.SeriesId.ToString(), reconciliation.TargetEntityId);
        var finalization = AcquisitionFinalizeJobPayload.Parse(reconciliation.PayloadJson!);
        Assert.Equal([harness.WantedEpisodeId], finalization.ImportedEntityIds);

        Assert.DoesNotContain(harness.Queue.Enqueued, request => request.Type == JobType.AutoIdentify);
    }

    [Fact]
    public async Task ImportWaitingForLibraryScanReportsItsBlockedState() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"],
            releaseTitle: "Show S01 1080p WEB-DL");
        var scanLease = await harness.ScanGate.EnterAsync(CancellationToken.None);

        var importTask = harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);
        try {
            var waiting = await WaitForProgressAsync(harness.Queue, "Waiting for the current library scan to finish.");

            Assert.Equal(0, waiting.Progress);
            Assert.False(importTask.IsCompleted);
            db.ChangeTracker.Clear();
            Assert.Equal(
                waiting.Message,
                await db.Acquisitions.AsNoTracking()
                    .Where(row => row.Id == harness.Import.Id)
                    .Select(row => row.StatusMessage)
                    .SingleAsync());
        } finally {
            await scanLease.DisposeAsync();
        }

        await importTask;
        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
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
            .Where(row => row.ParentEntityId == harness.SeasonId && row.KindCode == EntityKind.VideoEpisode.ToCode())
            .OrderBy(row => row.SortOrder)
            .ToArrayAsync();
        var episodeOne = Assert.Single(episodes, row => row.SortOrder == 1);
        var episodeTwo = Assert.Single(episodes, row => row.SortOrder == 2);

        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
        Assert.True(season.IsWanted);
        Assert.True(episodeOne.IsWanted);
        Assert.False(episodeTwo.IsWanted);
        Assert.True(await db.EntitySources.AsNoTracking().AnyAsync(row =>
            row.EntityId == season.Id
            && row.Code == EntitySourceCode.Folder.ToCode()
            && row.Value == harness.SeasonFolder));
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

        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
        Assert.NotNull((await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == harness.Import.Id)).ImportCheckpointJson);
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
                && episode.KindCode == EntityKind.VideoEpisode.ToCode()
                && episode.SortOrder >= 2
                && episode.SortOrder <= 4)
            .OrderBy(episode => episode.SortOrder)
            .ToArrayAsync();
        Assert.Equal<int?>([2, 3, 4], readyEpisodes.Select(episode => episode.SortOrder));
        Assert.All(readyEpisodes, episode => Assert.False(episode.IsWanted));
        Assert.All(readyEpisodes, episode => Assert.True(db.EntityFiles.AsNoTracking().Any(row =>
            row.EntityId == episode.Id && row.Role == EntityFileRole.Source)));
        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
        Assert.NotNull((await db.Acquisitions.AsNoTracking().SingleAsync(row => row.Id == harness.Import.Id)).ImportCheckpointJson);
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
        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
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
            preexistingTargetContent: "wrong-bytes");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Equal(
            "wrong-bytes",
            await File.ReadAllTextAsync(Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv")));
        Assert.Equal(
            "new-episode",
            await File.ReadAllTextAsync(Path.Combine(harness.Import.ContentPath!, "Show.S01E02.1080p.WEB-DL.mkv")));
        Assert.False(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E02 (2).mkv")));
    }

    [Fact]
    public async Task UnindexedIdenticalSeasonPackIsMaterializedWithoutCreatingDuplicates() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: [
                "Show.S01E02.1080p.WEB-DL.mkv",
                "Show.S01E03.1080p.WEB-DL.mkv"
            ],
            releaseTitle: "Show S01 1080p WEB-DL",
            payloadContent: "already-imported-episode",
            wantedEpisodeNumbers: [2, 3],
            preexistingTargetContent: "already-imported-episode");
        await File.WriteAllTextAsync(
            Path.Combine(harness.SeasonFolder, "Show - S01E03.mkv"),
            "already-imported-episode");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        var episodes = await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == harness.SeasonId && row.SortOrder >= 2 && row.SortOrder <= 3)
            .OrderBy(row => row.SortOrder)
            .ToArrayAsync();
        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
        Assert.Equal<int?>([2, 3], episodes.Select(row => row.SortOrder));
        Assert.All(episodes, episode => Assert.False(episode.IsWanted));
        Assert.All(episodes, episode => Assert.True(db.EntityFiles.AsNoTracking().Any(file =>
            file.EntityId == episode.Id && file.Role == EntityFileRole.Source)));
        Assert.False(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E02 (2).mkv")));
        Assert.False(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E03 (2).mkv")));
    }

    [Fact]
    public async Task ScanReconciledIdenticalSeasonPackFinishesInsteadOfBlocklistingTheRelease() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(
            db,
            ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: [
                "Show.S01E02.1080p.WEB-DL.mkv",
                "Show.S01E03.1080p.WEB-DL.mkv"
            ],
            releaseTitle: "Show S01 1080p WEB-DL",
            payloadContent: "already-imported-episode",
            wantedEpisodeNumbers: [2, 3]);
        var episodes = await db.Entities
            .Where(row => row.ParentEntityId == harness.SeasonId && row.SortOrder >= 2 && row.SortOrder <= 3)
            .OrderBy(row => row.SortOrder)
            .ToArrayAsync();
        var now = DateTimeOffset.UtcNow;
        foreach (var episode in episodes) {
            var target = Path.Combine(harness.SeasonFolder, $"Show - S01E{episode.SortOrder:00}.mkv");
            await File.WriteAllTextAsync(target, "already-imported-episode");
            episode.IsWanted = false;
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = episode.Id,
                Role = EntityFileRole.Source,
                Path = target,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync();

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
        Assert.Empty(await db.AcquisitionBlocklist.AsNoTracking().ToArrayAsync());
        Assert.All(episodes, episode => Assert.True(File.Exists(
            Path.Combine(harness.SeasonFolder, $"Show - S01E{episode.SortOrder:00}.mkv"))));
        Assert.False(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E02 (2).mkv")));
        Assert.False(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E03 (2).mkv")));
    }

    [Fact]
    public async Task WrongSeasonPayloadIsNotImportedForTheRequestedSeason() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S02E01.1080p.WEB-DL.mkv"], releaseTitle: "Show S02 1080p WEB-DL");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(harness.SeriesFolder, "Season 02", "Show - S02E01.mkv")));
        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
    }

    [Fact]
    public async Task MeasuredUpgradeReplacesAnOwnedEpisodeWhoseFilenameHasNoQuality() {
        await using var db = CreateContext();
        var inspector = new MeasuredUpgradeInspector(new(720, 1080, false, false, 1200, 1200));
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01.mkv",
            payloadFiles: ["Show.S01E01.1080p.WEB-DL.mkv"], releaseTitle: "Show S01 1080p WEB-DL",
            payloadContent: "measured-upgrade", upgradeInspector: inspector);
        var sourceId = (await db.EntityFiles.SingleAsync(row => row.EntityId == harness.OwnedEpisodeId)).Id;

        await harness.Engine.ImportAsync(harness.Context, harness.Import, default);

        Assert.Equal("measured-upgrade", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        Assert.Equal(sourceId, (await db.EntityFiles.SingleAsync(row => row.EntityId == harness.OwnedEpisodeId
            && row.Role == EntityFileRole.Source)).Id);
        Assert.Equal(1, inspector.Calls);
    }

    [Theory]
    [InlineData(2160, 1080, 1200, 1200)]
    [InlineData(720, 720, 1200, 1200)]
    [InlineData(720, 1080, 1200, 300)]
    [InlineData(720, 1080, 0, 1200)]
    [InlineData(0, 0, 0, 0)]
    public async Task MergedUpgradePreservesBothFilesWhenMeasuredEvidenceCannotSupportReplacement(
        int ownedResolution, int candidateResolution, double ownedRuntime, double candidateRuntime) {
        await using var db = CreateContext();
        var inspector = new MeasuredUpgradeInspector(ownedResolution == 0 ? null
            : new(ownedResolution, candidateResolution, false, false, ownedRuntime, candidateRuntime));
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E01.1080p.BluRay.mkv"], releaseTitle: "Show S01 1080p BluRay",
            upgradeInspector: inspector);

        await harness.Engine.ImportAsync(harness.Context, harness.Import, default);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Equal("owned-bytes", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        Assert.True(File.Exists(Path.Combine(harness.Import.ContentPath!, "Show.S01E01.1080p.BluRay.mkv")));
        Assert.Empty(await db.AcquisitionBlocklist.ToArrayAsync());
    }

    [Fact]
    public async Task StrictUpgradeReplacesTheOwnedFileInPlace() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S01E01.1080p.BluRay.mkv"], releaseTitle: "Show S01 1080p BluRay", payloadContent: "upgraded-bytes");

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
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
        Assert.DoesNotContain(harness.Queue.Enqueued, request => request.Type == JobType.ProbeVideo);
        Assert.Contains(harness.Queue.Enqueued, request =>
            request.Type == JobType.ReconcileEntity
            && request.TargetEntityId == harness.SeriesId.ToString());
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
            .Where(row => row.ParentEntityId == harness.SeasonId && row.KindCode == EntityKind.VideoEpisode.ToCode() && row.SortOrder == 1)
            .ToArrayAsync();
        var source = await db.EntityFiles.AsNoTracking()
            .SingleAsync(row => row.EntityId == harness.OwnedEpisodeId && row.Role == EntityFileRole.Source);

        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
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
                && row.KindCode == EntityKind.VideoEpisode.ToCode()
                && row.SortOrder == 1)
            .ToArrayAsync());
        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
    }

    [Theory]
    [InlineData("shared owner")]
    [InlineData("foreign season owner")]
    [InlineData("changed owner path")]
    [InlineData("split covered episodes")]
    public async Task PendingReplacementsRecheckPhysicalEpisodeOwnership(string scenario) {
        await using var db = CreateContext();
        var inspector = new MeasuredUpgradeInspector(new(720, 1080, false, false, 1200, 1200));
        var payloadName = scenario == "split covered episodes" ? "Show.S01E01E02.1080p.WEB-DL.mkv" : "Show.S01E01.1080p.WEB-DL.mkv";
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: [payloadName], releaseTitle: "Show S01 1080p WEB-DL", payloadContent: "incoming-upgrade",
            failSameFormatAfterStage: true, upgradeInspector: inspector);
        await Assert.ThrowsAsync<IOException>(() => harness.Engine.ImportAsync(harness.Context, harness.Import, default));
        var source = await db.EntityFiles.SingleAsync(row => row.EntityId == harness.OwnedEpisodeId && row.Role == EntityFileRole.Source);
        var otherPath = Path.Combine(harness.SeasonFolder, "other-owned.mkv");
        if (scenario == "changed owner path") {
            await File.WriteAllTextAsync(otherPath, "independently-owned-bytes");
            source.Path = otherPath;
        } else {
            var owner = harness.WantedEpisodeId;
            if (scenario == "foreign season owner") {
                var season = AddWantedEntity(db, EntityKind.VideoSeason.ToCode(), harness.SeriesId, 2);
                owner = AddWantedEntity(db, EntityKind.VideoEpisode.ToCode(), season, 1);
            }
            if (scenario == "split covered episodes") await File.WriteAllTextAsync(otherPath, "independently-owned-bytes");
            db.EntityFiles.Add(new EntityFileRow { Id = Guid.NewGuid(), EntityId = owner, Role = EntityFileRole.Source,
                Path = scenario == "split covered episodes" ? otherPath : harness.OwnedEpisodePath,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        var before = await db.EntityFiles.OrderBy(row => row.Id).Select(row => new { row.Id, row.Path }).ToArrayAsync();
        var resume = (await AcquisitionTestFactory.Store(db).GetImportContextAsync(harness.Import.Id, default))!;

        await harness.ResumeEngine.ImportAsync(harness.Context, resume, default);

        Assert.Equal(AcquisitionStatus.ManualImportRequired, await StatusOf(db, harness.Import.Id));
        Assert.Equal("owned-bytes", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        Assert.Equal("incoming-upgrade", await File.ReadAllTextAsync(Path.Combine(harness.Import.ContentPath!, payloadName)));
        if (File.Exists(otherPath)) Assert.Equal("independently-owned-bytes", await File.ReadAllTextAsync(otherPath));
        Assert.Equal(before, await db.EntityFiles.OrderBy(row => row.Id).Select(row => new { row.Id, row.Path }).ToArrayAsync());
        Assert.Equal(1, inspector.Calls);
    }

    [Theory]
    [InlineData("unchanged", false)]
    [InlineData("profile changed", true)]
    [InlineData("owned upgraded", true)]
    [InlineData("candidate lower resolution", true)]
    [InlineData("candidate shortened", true)]
    [InlineData("inspection failed", true)]
    [InlineData("no measured gain", true)]
    public async Task ResumingAPendingReplacementRechecksCurrentMeasuredEvidence(string scenario, bool held) {
        await using var db = CreateContext();
        MediaUpgradePayloadInspection? current = new(720, 1080, false, false, 1200, 1200);
        var inspector = new MeasuredUpgradeInspector(null) { Resolve = (_, _) => current };
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv",
            payloadFiles: ["Show.S01E01.1080p.WEB-DL.mkv"], releaseTitle: "Show S01 1080p WEB-DL",
            payloadContent: "incoming-upgrade", failSameFormatAfterStage: true, upgradeInspector: inspector);
        var sourceId = (await db.EntityFiles.SingleAsync(row => row.EntityId == harness.OwnedEpisodeId)).Id;
        await Assert.ThrowsAsync<IOException>(() => harness.Engine.ImportAsync(harness.Context, harness.Import, default));
        var resume = (await AcquisitionTestFactory.Store(db).GetImportContextAsync(harness.Import.Id, default))!;
        switch (scenario) {
            case "profile changed": db.BookAcquisitionProfiles.Add(new BookAcquisitionProfileRow { Id = Guid.NewGuid(),
                Kind = AcquisitionProfileKinds.For(EntityKind.VideoSeason), DisplayName = "UHD only", IsDefault = true,
                AllowedQualities = [VideoQuality.Webdl2160p.ToCode()], CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); break;
            case "owned upgraded": current = current with { OwnedResolutionTier = 2160 }; break;
            case "candidate lower resolution": current = current with { CandidateResolutionTier = 720 }; break;
            case "candidate shortened": current = current with { CandidateDurationSeconds = 300 }; break;
            case "inspection failed": current = null; break;
            case "no measured gain": current = current with { OwnedResolutionTier = 1080 }; break;
        }
        await db.SaveChangesAsync();

        await harness.ResumeEngine.ImportAsync(harness.Context, resume, default);

        Assert.Equal(held ? AcquisitionStatus.ManualImportRequired : AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
        Assert.Equal(held ? "owned-bytes" : "incoming-upgrade", await File.ReadAllTextAsync(harness.OwnedEpisodePath));
        Assert.Equal(sourceId, (await db.EntityFiles.SingleAsync(row => row.EntityId == harness.OwnedEpisodeId
            && row.Role == EntityFileRole.Source)).Id);
        if (held) {
            Assert.Equal("incoming-upgrade", await File.ReadAllTextAsync(Path.Combine(harness.Import.ContentPath!, "Show.S01E01.1080p.WEB-DL.mkv")));
            Assert.Null(Assert.Single((await AcquisitionTestFactory.Store(db).GetImportContextAsync(harness.Import.Id, default))!.TvImportCheckpoint!.Units).FinalPath);
        }
        Assert.Empty(await db.AcquisitionBlocklist.ToArrayAsync());
        Assert.Equal(2, inspector.Calls);
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
        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
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
        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
    }

    [Fact]
    public async Task AcquisitionWithoutEntityLinkKeepsTheTemplatePlacement() {
        await using var db = CreateContext();
        var harness = await HarnessAsync(db, ownedEpisodeName: "Show - s01e01 720p WEB.mkv", payloadFiles: ["Show.S01E02.1080p.WEB-DL.mkv"], releaseTitle: "Show S01 1080p WEB-DL", linkEntity: false);

        await harness.Engine.ImportAsync(harness.Context, harness.Import, CancellationToken.None);

        // Template path: a fresh series folder under the library root, not the existing tree.
        Assert.True(File.Exists(Path.Combine(harness.LibraryRoot, "Show", "Season 01", "Show - S01E02.mkv")));
        Assert.False(File.Exists(Path.Combine(harness.SeasonFolder, "Show - S01E02.mkv")));
        Assert.Equal(AcquisitionStatus.Importing, await StatusOf(db, harness.Import.Id));
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
        Guid SeriesId,
        Guid SeasonId,
        Guid OwnedEpisodeId,
        Guid WantedEpisodeId,
        MergedImportTestSupport.RecordingJobQueue Queue,
        VideoScanConcurrencyGate ScanGate);

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
        bool autoGenerateMetadata = false,
        bool enableMissingFallback = false,
        Action? beforeCheckpoint = null,
        IMediaUpgradePayloadInspector? upgradeInspector = null,
        IMediaProbe? mediaProbe = null, IVideoPayloadVerifier? videoVerifier = null) {
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
        var seriesId = AddFolderEntity(db, EntityKind.VideoSeries.ToCode(), null, null, seriesFolder);
        var seasonId = deletedSeason
            ? AddWantedEntity(db, EntityKind.VideoSeason.ToCode(), seriesId, 1)
            : AddFolderEntity(db, EntityKind.VideoSeason.ToCode(), seriesId, 1, seasonFolder);
        Guid ownedEpisodeId;
        if (deletedSeason) {
            ownedEpisodeId = Guid.Empty;
            AddWantedEntity(db, EntityKind.VideoEpisode.ToCode(), seasonId, 1);
        } else {
            ownedEpisodeId = AddEntity(db, EntityKind.VideoEpisode.ToCode(), seasonId, 1, ownedEpisodePath);
        }
        var wantedIds = (wantedEpisodeNumbers ?? [2])
            .Distinct()
            .ToDictionary(
                episodeNumber => episodeNumber,
                episodeNumber => AddWantedEntity(db, EntityKind.VideoEpisode.ToCode(), seasonId, episodeNumber));
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
        IMonitorStore? monitorStore = new EfMonitorStore(db);
        if (enableMissingFallback) {
            var enabledMonitorStore = new EfMonitorStore(db);
            await enabledMonitorStore.StartAsync(
                acquisitionId,
                EntityKind.VideoSeason,
                "Show",
                author: null,
                CancellationToken.None);
            monitorStore = enabledMonitorStore;
        }

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
            hints,
            NullLogger<ImportedVideoMaterializer>.Instance,
            snapshots: new EfScanSnapshotStore(db),
            processingRoots: scanPersistence);
        IImportedVideoMaterializer firstMaterializer = failMaterialization
            ? new FailOnCallImportedVideoMaterializer(realMaterializer, 1)
            : realMaterializer;
        var realMover = new ImportFileMover();
        IImportFileMover firstMover = failPlacementOnCall is { } failBeforeCall
            ? new FailOnCallImportFileMover(realMover, failBeforeCall, failAfterPlacement: false)
            : failAfterPlacementOnCall is { } failAfterCall
                ? new FailOnCallImportFileMover(realMover, failAfterCall, failAfterPlacement: true)
                : realMover;
        if (beforeCheckpoint is not null) {
            firstMover = new BeforeCheckpointMover(firstMover, beforeCheckpoint);
        }
        IOwnedFileReplacer firstReplacer = failCrossFormatAfterInstall
            ? new ThrowAfterCrossFormatInstallReplacer()
            : failSameFormatAfterStage
                ? new ThrowAfterReplacementStageReplacer()
                : failAfterReplacementEvidence
                    ? new FailAfterReplacementEvidenceReplacer()
                    : new OwnedFileReplacer(new MergedImportTestSupport.NoRecycleBin(), NullLogger<OwnedFileReplacer>.Instance, new TestVideoPayloadVerifier());
        var resumeReplacer = new OwnedFileReplacer(
            new MergedImportTestSupport.NoRecycleBin(),
            NullLogger<OwnedFileReplacer>.Instance, new TestVideoPayloadVerifier());
        var scanGate = new VideoScanConcurrencyGate();
        var engine = CreateEngine(firstMaterializer, firstMover, firstReplacer);
        var resumeEngine = CreateEngine(realMaterializer, realMover, resumeReplacer);

        TvAcquisitionImportEngine CreateEngine(
            IImportedVideoMaterializer importedVideoMaterializer,
            IImportFileMover importFileMover,
            IOwnedFileReplacer ownedFileReplacer) => new(
            store,
            new EfBookAcquisitionProfileStore(db),
            rootPersistence,
            new DownloadPayloadReader(),
            importFileMover,
            new DownloadClientCleanupService(store, new MergedImportTestSupport.ThrowingClientConfigStore(), new MergedImportTestSupport.ThrowingClientFactory(), NullLogger<DownloadClientCleanupService>.Instance),
            new EfImportTargetIndex(db),
            ownedFileReplacer,
            new EfAcquisitionBlocklistStore(db),
            history,
            importedVideoMaterializer,
            scanGate,
            NullLogger<TvAcquisitionImportEngine>.Instance,
            upgradeInspector ?? new MeasuredUpgradeInspector(null) {
                Resolve = (owned, candidate) => new(
                    Resolution(owned) ?? Resolution(releaseTitle) ?? 720,
                    Resolution(candidate) ?? Resolution(releaseTitle) ?? 720,
                    false, false, 1200, 1200)
            },
            mediaProbe ?? new NewEpisodeProbe(new(1200, 1000, 3840, 2160, 24, null, null, null, null, null, null)),
            videoVerifier ?? new TestVideoPayloadVerifier(),
            monitorStore);

        static int? Resolution(string title) => MediaQualityLadder.VideoResolutionTierOf(VideoQualityDetection.Detect(title).ToCode());

        var job = new JobRunSnapshot(
            jobId, JobType.AcquisitionImport, JobRunStatus.Running, 0, null, "{}",
            null, null, null, now, now, null);
        var queue = new MergedImportTestSupport.RecordingJobQueue();
        return new Harness(
            engine, resumeEngine, new JobContext(job, queue), import,
            libraryRoot, seriesFolder, seasonFolder, ownedEpisodePath, seriesId, seasonId, ownedEpisodeId, wantedEpisodeId,
            queue, scanGate);
    }

    private static async Task<(int Progress, string? Message)> WaitForProgressAsync(
        MergedImportTestSupport.RecordingJobQueue queue,
        string message) {
        for (var attempt = 0; attempt < 100; attempt++) {
            var update = queue.ProgressUpdates.FirstOrDefault(item => item.Message == message);
            if (update.Message is not null) {
                return update;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"The import never reported '{message}'.");
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

    private static Guid AddFolderEntity(PrismediaDbContext db, string kindCode, Guid? parent, int? sortOrder, string folderPath) {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Entities.Add(new EntityRow {
            Id = id, KindCode = kindCode, Title = kindCode, ParentEntityId = parent,
            SortOrder = sortOrder, CreatedAt = now, UpdatedAt = now
        });
        db.EntitySources.Add(new EntitySourceRow {
            EntityId = id,
            Code = EntitySourceCode.Folder.ToCode(),
            Value = folderPath,
            UpdatedAt = now
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

    private sealed class NewEpisodeProbe(VideoProbeData? video) : IMediaProbe {
        public List<string> Paths { get; } = [];
        public Func<string, VideoProbeData?>? Resolve { get; set; }
        public Task<VideoProbeData?> ProbeVideoAsync(string path, CancellationToken token) {
            Paths.Add(path);
            return Task.FromResult(Resolve is null ? video : Resolve(path));
        }
        public Task<AudioProbeData?> ProbeAudioAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public Task<ImageProbeData?> ProbeImageAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<SubtitleStreamData>> ProbeSubtitleStreamsAsync(string path, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class MeasuredUpgradeInspector(MediaUpgradePayloadInspection? result) : IMediaUpgradePayloadInspector {
        public int Calls { get; private set; }
        public Func<string, string, MediaUpgradePayloadInspection?>? Resolve { get; init; }
        public Task<MediaUpgradePayloadInspection?> InspectAsync(string ownedContentPath, string candidateContentPath,
            CancellationToken cancellationToken) {
            Calls++;
            Assert.True(File.Exists(ownedContentPath));
            Assert.True(File.Exists(candidateContentPath));
            return Task.FromResult(Resolve is null ? result : Resolve(ownedContentPath, candidateContentPath));
        }
    }

    private sealed class FailOnCallImportedVideoMaterializer(
        IImportedVideoMaterializer inner,
        int failingCall) : IImportedVideoMaterializer {
        private int _calls;

        public Task<ImportedEntityMaterializationResult> MaterializeAsync(
            JobContext context,
            ImportedTvMaterializationRequest request,
            CancellationToken cancellationToken) {
            _calls++;
            return _calls == failingCall
                ? throw new InvalidOperationException("Synthetic catalog failure.")
                : inner.MaterializeAsync(context, request, cancellationToken);
        }
    }

    private sealed class BeforeCheckpointMover(IImportFileMover inner, Action beforeCheckpoint) : IImportFileMover {
        public string ResolveExactTargetPath(string desiredTargetPath, IReadOnlyCollection<string> reservedTargetPaths) {
            beforeCheckpoint();
            return inner.ResolveExactTargetPath(desiredTargetPath, reservedTargetPaths);
        }

        public Task<string> PlaceAsync(ResolvedImportItem item, ImportMode mode, CancellationToken cancellationToken) =>
            inner.PlaceAsync(item, mode, cancellationToken);

        public Task<string> PlaceExactAsync(ResolvedImportItem item, ImportMode mode, CancellationToken cancellationToken) =>
            inner.PlaceExactAsync(item, mode, cancellationToken);
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
            EntityKind kind,
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
            EntityKind kind,
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
            EntityKind kind,
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
            EntityKind kind,
            bool allowFormatChange = false) =>
            Task.FromResult(OwnedFileReplaceResult.Failed("The durable overload was expected."));

        public Task<OwnedFileReplaceResult> ReplaceRetainingBackupAsync(
            string ownedFolder,
            string newContentPath,
            BookFormatTier ownedFormatTier,
            CancellationToken cancellationToken,
            EntityKind kind,
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
