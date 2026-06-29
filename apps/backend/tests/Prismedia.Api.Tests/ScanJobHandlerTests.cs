using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class ScanJobHandlerTests {
    [Fact]
    public async Task VideoScanEnqueuesPreviewJobWhenOnlyTrickplayNeedsGeneration() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: false,
                AutoGenerateOshash: false,
                AutoGenerateMd5: false,
                AutoGeneratePreview: false,
                GenerateTrickplay: true,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2),
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: true,
                    NeedsSubtitleExtraction: false,    NeedsGridThumbnail: false)
            }
        };
        var discovery = new RecordingFileDiscovery(["/media/videos/movie.mkv"]);
        var queue = new RecordingJobQueue();
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);

        var request = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.GeneratePreview, request.Type);
        Assert.Equal(videoId.ToString(), request.TargetEntityId);
    }

    [Fact]
    public async Task VideoScanRemovesOrphanTagsWhenSettingEnabled() {
        var persistence = new FakeScanPersistence([OrphanCleanupRoot]) {
            Settings = OrphanCleanupSettings(removeOrphanTags: true),
        };
        var handler = OrphanCleanupHandler(persistence);

        await handler.HandleAsync(
            new JobContext(OrphanCleanupJob, new RecordingJobQueue()), CancellationToken.None);

        // Cleanup runs once per scan job via AfterScanAsync, so it fires even though this root has
        // no files and the detailed pass does nothing.
        Assert.Equal(1, persistence.RemoveOrphanTagsCalls);
    }

    [Fact]
    public async Task VideoScanSkipsOrphanTagRemovalWhenSettingDisabled() {
        var persistence = new FakeScanPersistence([OrphanCleanupRoot]) {
            Settings = OrphanCleanupSettings(removeOrphanTags: false),
        };
        var handler = OrphanCleanupHandler(persistence);

        await handler.HandleAsync(
            new JobContext(OrphanCleanupJob, new RecordingJobQueue()), CancellationToken.None);

        Assert.Equal(0, persistence.RemoveOrphanTagsCalls);
    }

    private static readonly LibraryRootData OrphanCleanupRoot = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "/media/videos",
        "Videos",
        Enabled: true,
        Recursive: true,
        ScanVideos: true,
        ScanImages: false,
        ScanAudio: false,
        ScanBooks: false,
        IsNsfw: false);

    private static LibrarySettingsData OrphanCleanupSettings(bool removeOrphanTags) => new(
        AutoGenerateMetadata: false,
        AutoGenerateOshash: false,
        AutoGenerateMd5: false,
        AutoGeneratePreview: false,
        GenerateTrickplay: false,
        TrickplayIntervalSeconds: 10,
        PreviewClipDurationSeconds: 8,
        ThumbnailQuality: 2,
        TrickplayQuality: 2,
        RemoveOrphanTags: removeOrphanTags);

    private static ScanLibraryJobHandler OrphanCleanupHandler(FakeScanPersistence persistence) =>
        new(NullLogger<ScanLibraryJobHandler>.Instance, new RecordingFileDiscovery([]), persistence, persistence, persistence);

    private static JobRunSnapshot OrphanCleanupJob => new(
        Guid.NewGuid(),
        JobType.ScanLibrary,
        JobRunStatus.Running,
        Progress: 0,
        Message: null,
        PayloadJson: $$"""{"libraryRootId":"{{OrphanCleanupRoot.Id}}"}""",
        TargetEntityKind: "library-root",
        TargetEntityId: OrphanCleanupRoot.Id.ToString(),
        TargetLabel: OrphanCleanupRoot.Label,
        CreatedAt: DateTimeOffset.UtcNow,
        StartedAt: DateTimeOffset.UtcNow,
        FinishedAt: null);

    [Fact]
    public async Task VideoScanEnqueuesAutoIdentifyWhenEnabledForVideoKind() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: false,
                AutoGenerateOshash: false,
                AutoGenerateMd5: false,
                AutoGeneratePreview: false,
                GenerateTrickplay: false,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2,
                AutoIdentifyEnabled: true,
                AutoIdentifyKinds: ["video"]),
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false, NeedsGridThumbnail: false)
            }
        };
        var discovery = new RecordingFileDiscovery(["/media/videos/movie.mkv"]);
        var queue = new RecordingJobQueue();
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);

        var request = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.AutoIdentify, request.Type);
        Assert.Equal(videoId.ToString(), request.TargetEntityId);
    }

    [Fact]
    public async Task VideoScanSkipsAlreadyOrganizedAutoIdentifyRootsWhenUnorganizedOnly() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: false,
                AutoGenerateOshash: false,
                AutoGenerateMd5: false,
                AutoGeneratePreview: false,
                GenerateTrickplay: false,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2,
                AutoIdentifyEnabled: true,
                AutoIdentifyKinds: ["video"],
                AutoIdentifyUnorganizedOnly: true),
            UpsertedVideoIds = [videoId],
            AutoIdentifyRootTargets = [new AutoIdentifyRootTarget(videoId, "video", "Sonic X", IsOrganized: true)],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false, NeedsGridThumbnail: false)
            }
        };
        var queue = new RecordingJobQueue();
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            new RecordingFileDiscovery(["/media/videos/sonic-x.mkv"]),
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);

        Assert.DoesNotContain(queue.Enqueued, request => request.Type == JobType.AutoIdentify);
    }

    [Fact]
    public async Task VideoScanSkipsAutoIdentifyWhenRootOptsOut() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false,
            AutoIdentify: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            // Global Auto Identify is on for video, but this root opts out, so nothing is enqueued.
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: false,
                AutoGenerateOshash: false,
                AutoGenerateMd5: false,
                AutoGeneratePreview: false,
                GenerateTrickplay: false,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2,
                AutoIdentifyEnabled: true,
                AutoIdentifyKinds: ["video"]),
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false, NeedsGridThumbnail: false)
            }
        };
        var queue = new RecordingJobQueue();
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            new RecordingFileDiscovery(["/media/videos/movie.mkv"]),
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);

        Assert.DoesNotContain(queue.Enqueued, request => request.Type == JobType.AutoIdentify);
    }

    [Fact]
    public async Task VideoScanSkipsAutoIdentifyWhenKindNotSelected() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: false,
                AutoGenerateOshash: false,
                AutoGenerateMd5: false,
                AutoGeneratePreview: false,
                GenerateTrickplay: false,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2,
                AutoIdentifyEnabled: true,
                AutoIdentifyKinds: ["audio"]),
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false, NeedsGridThumbnail: false)
            }
        };
        var discovery = new RecordingFileDiscovery(["/media/videos/movie.mkv"]);
        var queue = new RecordingJobQueue();
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task AudioScanEnqueuesWaveformForAlreadyProbedTrackMissingWaveform() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/music",
            "Music",
            Enabled: true,
            Recursive: true,
            ScanVideos: false,
            ScanImages: false,
            ScanAudio: true,
            ScanBooks: false,
            IsNsfw: false);
        var sourcePath = "/media/music/Album/song.flac";
        var persistence = new FakeScanPersistence([root]) {
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: true,
                AutoGenerateOshash: false,
                AutoGenerateMd5: false,
                AutoGeneratePreview: true,
                GenerateTrickplay: false,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2),
            HasTechnical = true
        };
        var queue = new RecordingJobQueue();
        var handler = new ScanAudioJobHandler(
            NullLogger<ScanAudioJobHandler>.Instance,
            new RecordingFileDiscovery([sourcePath]),
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanAudio,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);

        var track = Assert.Single(persistence.UpsertedAudioTracks);
        var request = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.GenerateAudioWaveform, request.Type);
        Assert.Equal(EntityKindRegistry.AudioTrack.Code, request.TargetEntityKind);
        Assert.Equal(track.Id.ToString(), request.TargetEntityId);
    }

    [Fact]
    public async Task AudioScanEnqueuesWaveformForUnchangedExistingTrackMissingWaveform() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/music",
            "Music",
            Enabled: true,
            Recursive: true,
            ScanVideos: false,
            ScanImages: false,
            ScanAudio: true,
            ScanBooks: false,
            IsNsfw: false);
        var sourcePath = "/media/music/Album/song.flac";
        var trackId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: true,
                AutoGenerateOshash: false,
                AutoGenerateMd5: false,
                AutoGeneratePreview: true,
                GenerateTrickplay: false,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2),
            ExistingAudioTrackTargets = [new EntityRefreshTarget(trackId, EntityKindRegistry.AudioTrack.Code, "song")],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [trackId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: true,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,
                    NeedsGridThumbnail: false)
            }
        };
        var snapshots = new FakeScanSnapshotStore();
        snapshots.Seed(root.Id, JobType.ScanAudio.ToCode(), [sourcePath]);
        var queue = new RecordingJobQueue();
        var handler = new ScanAudioJobHandler(
            NullLogger<ScanAudioJobHandler>.Instance,
            new RecordingFileDiscovery([sourcePath]),
            persistence,
            persistence,
            persistence,
            snapshots);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanAudio,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);

        var request = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.GenerateAudioWaveform, request.Type);
        Assert.Equal(EntityKindRegistry.AudioTrack.Code, request.TargetEntityKind);
        Assert.Equal(trackId.ToString(), request.TargetEntityId);
    }

    [Fact]
    public async Task AllRootsScanSkipsRootDeletedAfterInitialListing() {
        var activeRoot = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/active",
            "Active",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var deletedRoot = new LibraryRootData(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "/media/deleted",
            "Deleted",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var persistence = new FakeScanPersistence([activeRoot, deletedRoot]) {
            DeletedRootIds = new HashSet<Guid> { deletedRoot.Id }
        };
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            new RecordingFileDiscovery([]),
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: "{}",
            TargetEntityKind: null,
            TargetEntityId: null,
            TargetLabel: null,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        Assert.Equal([activeRoot.Id, deletedRoot.Id], persistence.LoadedRootIds);
        Assert.Equal([activeRoot.Id], persistence.LastScannedRootIds);
    }

    [Fact]
    public async Task VideoScanClassifiesSeasonFolderEpisodesForHierarchyMaterialization() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: false,
                AutoGenerateOshash: false,
                AutoGenerateMd5: false,
                AutoGeneratePreview: false,
                GenerateTrickplay: false,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2),
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,    NeedsGridThumbnail: false)
            }
        };
        var discovery = new RecordingFileDiscovery([
            "/media/videos/The Chair Company/Season 1/The Chair Company - S01E02 - New Blood.mkv"
        ]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        var item = Assert.Single(persistence.UpsertedVideoItems);
        Assert.Equal("The Chair Company", item.Series?.Title);
        Assert.Equal("/media/videos/The Chair Company", item.Series?.FolderPath);
        Assert.Equal("Season 1", item.Season?.Title);
        Assert.Equal("/media/videos/The Chair Company/Season 1", item.Season?.FolderPath);
        Assert.Equal(1, item.Season?.SeasonNumber);
        Assert.Equal(2, item.EpisodeNumber);
    }

    [Fact]
    public async Task VideoScanClassifiesNamedNestedEpisodeFoldersAsSeasons() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings,
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,    NeedsGridThumbnail: false)
            }
        };
        var discovery = new RecordingFileDiscovery([
            "/media/videos/Blue's Clues/Specials/Blue's Clues - S00E100 - Behind the Clues - 10 Years of Blue SDTV.mp4"
        ]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        var item = Assert.Single(persistence.UpsertedVideoItems);
        Assert.Equal("Blue's Clues", item.Series?.Title);
        Assert.Equal("/media/videos/Blue's Clues", item.Series?.FolderPath);
        Assert.Equal("Specials", item.Season?.Title);
        Assert.Equal("/media/videos/Blue's Clues/Specials", item.Season?.FolderPath);
        Assert.Equal(0, item.Season?.SeasonNumber);
        Assert.Equal(100, item.EpisodeNumber);
    }

    [Fact]
    public async Task VideoScanKeepsRootLevelEpisodeFilesDirectlyUnderSeries() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings,
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,    NeedsGridThumbnail: false)
            }
        };
        var discovery = new RecordingFileDiscovery([
            "/media/videos/Blue's Clues/Blue's Clues - S01E01 - Snack Time SDTV.mkv"
        ]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        var item = Assert.Single(persistence.UpsertedVideoItems);
        Assert.Equal("Blue's Clues", item.Series?.Title);
        Assert.Equal("/media/videos/Blue's Clues", item.Series?.FolderPath);
        Assert.Null(item.Season);
        Assert.Equal(1, item.EpisodeNumber);
    }

    [Fact]
    public async Task VideoScanClassifiesSingleSameNamedFolderFileAsMovie() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings,
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,
                    NeedsGridThumbnail: false)
            }
        };
        var discovery = new RecordingFileDiscovery([
            "/media/videos/Friendship/Friendship.mp4"
        ]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        var item = Assert.Single(persistence.UpsertedVideoItems);
        Assert.Equal("Friendship", item.Movie?.Title);
        Assert.Equal("/media/videos/Friendship", item.Movie?.FolderPath);
        Assert.Null(item.Series);
        Assert.Null(item.Season);
        Assert.Null(item.EpisodeNumber);
        Assert.Equal(["/media/videos/Friendship"], persistence.ValidMoviePaths);
    }

    [Fact]
    public async Task VideoScanClassifiesMovieFolderWithReleaseSuffixAndGeneratedArtifacts() {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"prismedia-movie-scan-{Guid.NewGuid():N}");
        try {
            var movieFolder = Path.Combine(tempRoot, "Friendship (2025)");
            Directory.CreateDirectory(Path.Combine(movieFolder, "Friendship (2025) Bluray-1080p.trickplay"));
            File.WriteAllText(Path.Combine(movieFolder, "movie.nfo"), "<movie />");
            File.WriteAllText(Path.Combine(movieFolder, "folder.jpg"), "poster");
            var videoPath = Path.Combine(movieFolder, "Friendship (2025) Bluray-1080p.mp4");
            File.WriteAllText(videoPath, "video");

            var root = new LibraryRootData(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                tempRoot,
                "Videos",
                Enabled: true,
                Recursive: true,
                ScanVideos: true,
                ScanImages: false,
                ScanAudio: false,
                ScanBooks: false,
                IsNsfw: false);
            var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var persistence = new FakeScanPersistence([root]) {
                Settings = DisabledGeneratedWorkSettings,
                UpsertedVideoIds = [videoId],
                DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                    [videoId] = new(
                        NeedsProbe: false,
                        MissingOshash: false,
                        MissingMd5: false,
                        NeedsPreview: false,
                        NeedsTrickplay: false,
                        NeedsSubtitleExtraction: false,
                        NeedsGridThumbnail: false)
                }
            };
            var handler = new ScanLibraryJobHandler(
                NullLogger<ScanLibraryJobHandler>.Instance,
                new RecordingFileDiscovery([videoPath]),
                persistence,
                persistence,
                persistence);
            var job = new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.ScanLibrary,
                JobRunStatus.Running,
                Progress: 0,
                Message: null,
                PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
                TargetEntityKind: "library-root",
                TargetEntityId: root.Id.ToString(),
                TargetLabel: root.Label,
                CreatedAt: DateTimeOffset.UtcNow,
                StartedAt: DateTimeOffset.UtcNow,
                FinishedAt: null);

            await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

            var item = Assert.Single(persistence.UpsertedVideoItems);
            Assert.Equal("Friendship (2025)", item.Movie?.Title);
            Assert.Equal(movieFolder, item.Movie?.FolderPath);
            Assert.Null(item.Series);
            Assert.Null(item.Season);
            Assert.Equal([movieFolder], persistence.ValidMoviePaths);
        }
        finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VideoScanClassifiesParenthesizedEpisodeTokensDirectlyUnderSeries() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings,
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,
                    NeedsGridThumbnail: false)
            }
        };
        // Episodes named "(S1E1)" with the token wrapped in parentheses and no Season subfolder must
        // still route to series handling rather than falling through to a loose Video.
        var discovery = new RecordingFileDiscovery([
            "/media/videos/Dragon Ball Super/Dragon Ball Super (S1E1).mp4"
        ]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        var item = Assert.Single(persistence.UpsertedVideoItems);
        Assert.Equal("Dragon Ball Super", item.Series?.Title);
        Assert.Equal("/media/videos/Dragon Ball Super", item.Series?.FolderPath);
        Assert.Null(item.Season);
        Assert.Equal(1, item.EpisodeNumber);
        Assert.Null(item.Movie);
        Assert.Empty(persistence.ValidMoviePaths);
    }

    [Fact]
    public async Task VideoScanClassifiesSingleFolderFileAsMovieWhenFilenameDiffersFromFolder() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings,
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,
                    NeedsGridThumbnail: false)
            }
        };
        // Standard Radarr/Jellyfin layout: clean folder name, release-style filename that does not begin
        // with the folder name (accents, dot separators). It must still classify as a movie.
        var discovery = new RecordingFileDiscovery([
            "/media/videos/Pokémon - The First Movie (1998)/Pokemon.The.First.Movie.1998.DUBBED.1080p.BluRay.REMUX-DDB.mkv"
        ]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        var item = Assert.Single(persistence.UpsertedVideoItems);
        Assert.Equal("Pokémon - The First Movie (1998)", item.Movie?.Title);
        Assert.Equal("/media/videos/Pokémon - The First Movie (1998)", item.Movie?.FolderPath);
        Assert.Null(item.Series);
        Assert.Null(item.Season);
        Assert.Equal(["/media/videos/Pokémon - The First Movie (1998)"], persistence.ValidMoviePaths);
    }

    [Fact]
    public async Task VideoScanClassifiesMovieFolderWithHiddenArtifactDirectory() {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"prismedia-movie-scan-{Guid.NewGuid():N}");
        try {
            var movieFolder = Path.Combine(tempRoot, "A Bug's Life (1998)");
            // A stray hidden directory left by another tool (".thumbs") plus a sibling "*.trickplay"
            // artifact directory must not disqualify the lone movie file in the folder.
            Directory.CreateDirectory(Path.Combine(movieFolder, ".thumbs"));
            Directory.CreateDirectory(Path.Combine(movieFolder, "A Bug's Life (1998) Bluray-2160p.trickplay"));
            File.WriteAllText(Path.Combine(movieFolder, ".thumbs", "A Bug's Life (1998) Bluray-2160p.jpg"), "thumb");
            File.WriteAllText(Path.Combine(movieFolder, "movie.nfo"), "<movie />");
            var videoPath = Path.Combine(movieFolder, "A Bug's Life (1998) Bluray-2160p.mkv");
            File.WriteAllText(videoPath, "video");

            var root = new LibraryRootData(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                tempRoot,
                "Videos",
                Enabled: true,
                Recursive: true,
                ScanVideos: true,
                ScanImages: false,
                ScanAudio: false,
                ScanBooks: false,
                IsNsfw: false);
            var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var persistence = new FakeScanPersistence([root]) {
                Settings = DisabledGeneratedWorkSettings,
                UpsertedVideoIds = [videoId],
                DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                    [videoId] = new(
                        NeedsProbe: false,
                        MissingOshash: false,
                        MissingMd5: false,
                        NeedsPreview: false,
                        NeedsTrickplay: false,
                        NeedsSubtitleExtraction: false,
                        NeedsGridThumbnail: false)
                }
            };
            var handler = new ScanLibraryJobHandler(
                NullLogger<ScanLibraryJobHandler>.Instance,
                new RecordingFileDiscovery([videoPath]),
                persistence,
                persistence,
                persistence);
            var job = new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.ScanLibrary,
                JobRunStatus.Running,
                Progress: 0,
                Message: null,
                PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
                TargetEntityKind: "library-root",
                TargetEntityId: root.Id.ToString(),
                TargetLabel: root.Label,
                CreatedAt: DateTimeOffset.UtcNow,
                StartedAt: DateTimeOffset.UtcNow,
                FinishedAt: null);

            await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

            var item = Assert.Single(persistence.UpsertedVideoItems);
            Assert.Equal("A Bug's Life (1998)", item.Movie?.Title);
            Assert.Equal(movieFolder, item.Movie?.FolderPath);
            Assert.Null(item.Series);
            Assert.Null(item.Season);
            Assert.Equal([movieFolder], persistence.ValidMoviePaths);
        }
        finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VideoScanKeepsLibraryRootFilesAsVideos() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings,
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,
                    NeedsGridThumbnail: false)
            }
        };
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            new RecordingFileDiscovery(["/media/videos/Friendship.mp4"]),
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        var item = Assert.Single(persistence.UpsertedVideoItems);
        Assert.Null(item.Movie);
        Assert.Null(item.Series);
        Assert.Null(item.Season);
        Assert.Empty(persistence.ValidMoviePaths);
    }

    [Fact]
    public async Task VideoScanKeepsFolderFileAsSeriesWhenFolderHasNestedVideoFiles() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var ids = new[] {
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333")
        };
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings,
            UpsertedVideoIds = ids,
            DownstreamNeedsById = ids.ToDictionary(
                id => id,
                _ => new DownstreamNeeds(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,
                    NeedsGridThumbnail: false))
        };
        var discovery = new RecordingFileDiscovery([
            "/media/videos/Friendship/Friendship.mp4",
            "/media/videos/Friendship/Season 1/Friendship - S01E01.mp4"
        ]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        Assert.All(persistence.UpsertedVideoItems, item => Assert.Null(item.Movie));
        Assert.Empty(persistence.ValidMoviePaths);
    }

    [Fact]
    public async Task VideoScanGroupsMultiVideoFolderWithoutNumberingAsSeries() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var ids = new[] {
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333")
        };
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings,
            UpsertedVideoIds = ids,
            DownstreamNeedsById = ids.ToDictionary(
                id => id,
                _ => new DownstreamNeeds(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,
                    NeedsGridThumbnail: false))
        };
        // Discovery order is intentionally reversed from filename order to prove sort-by-filename.
        var discovery = new RecordingFileDiscovery([
            "/media/videos/Clips/Beta clip.mp4",
            "/media/videos/Clips/Alpha clip.mp4"
        ]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        Assert.All(persistence.UpsertedVideoItems, item => {
            Assert.Equal("Clips", item.Series?.Title);
            Assert.Equal("/media/videos/Clips", item.Series?.FolderPath);
            Assert.Null(item.Season);
            Assert.Null(item.EpisodeNumber);
            Assert.Null(item.Movie);
        });
        Assert.Empty(persistence.ValidMoviePaths);

        var beta = Assert.Single(persistence.UpsertedVideoItems, item => item.FilePath.EndsWith("Beta clip.mp4"));
        var alpha = Assert.Single(persistence.UpsertedVideoItems, item => item.FilePath.EndsWith("Alpha clip.mp4"));
        Assert.Equal(0, alpha.FolderSortOrder);
        Assert.Equal(1, beta.FolderSortOrder);
    }

    [Fact]
    public async Task VideoScanPassesRootExclusionsToDiscovery() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings,
            UpsertedVideoIds = [videoId],
            ExcludedPathsByRoot = new Dictionary<Guid, IReadOnlySet<string>> {
                [root.Id] = new HashSet<string>(["/media/videos/Skip"], StringComparer.OrdinalIgnoreCase)
            },
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,    NeedsGridThumbnail: false)
            }
        };
        var discovery = new RecordingFileDiscovery(["/media/videos/Keep/movie.mkv"]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        Assert.Equal(["/media/videos/Skip"], discovery.LastExcludedPaths);
        Assert.Equal("/media/videos/Keep/movie.mkv", Assert.Single(persistence.UpsertedVideoItems).FilePath);
    }

    [Fact]
    public async Task VideoScanReadsAndAppliesSidecarMetadata() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos",
            "Videos",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var metadata = new VideoSidecarMetadata {
            Title = "Sidecar Title",
            Description = "Sidecar plot",
            Date = "2026-05-01",
            Studio = "Sidecar Studio",
            Tags = ["Noir", "Feature"],
            Performers = ["Ada Actor"],
            Urls = ["https://example.test/video"]
        };
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings,
            UpsertedVideoIds = [videoId],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,
                    NeedsGridThumbnail: false)
            }
        };
        var metadataPersistence = new RecordingScanMetadataPersistence();
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            new RecordingFileDiscovery(["/media/videos/movie.mkv"]),
            persistence,
            persistence,
            persistence,
            sidecars: new StubVideoSidecarMetadataReader(metadata),
            scanMetadata: metadataPersistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        var item = Assert.Single(persistence.UpsertedVideoItems);
        Assert.Equal("Sidecar Title", item.Title);
        Assert.Same(metadata, item.Metadata);
        var applied = Assert.Single(metadataPersistence.AppliedVideos);
        Assert.Equal(videoId, applied.EntityId);
        Assert.Equal("movie", applied.FallbackTitle);
        Assert.Same(metadata, applied.Metadata);
    }

    [Fact]
    public async Task GalleryScanTreatsRootFilesAsLooseAndFoldersAsNestedGalleries() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/images",
            "Images",
            Enabled: true,
            Recursive: true,
            ScanVideos: false,
            ScanImages: true,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings
        };
        var discovery = new RecordingFileDiscovery(
            directoryGroups: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
                ["/media/images"] = ["/media/images/root.png"],
                ["/media/images/Gallery"] = ["/media/images/Gallery/a.png", "/media/images/Gallery/a2.png"],
                ["/media/images/Gallery/A secondGallery"] = ["/media/images/Gallery/A secondGallery/b.png", "/media/images/Gallery/A secondGallery/b2.png"]
            });
        var handler = new ScanGalleryJobHandler(
            NullLogger<ScanGalleryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanGallery,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        Assert.DoesNotContain(persistence.UpsertedGalleries, gallery => gallery.FolderPath == root.Path);
        var gallery = Assert.Single(persistence.UpsertedGalleries, item => item.FolderPath == "/media/images/Gallery");
        var nestedGallery = Assert.Single(persistence.UpsertedGalleries, item => item.FolderPath == "/media/images/Gallery/A secondGallery");
        Assert.Null(gallery.ParentGalleryEntityId);
        Assert.Equal(0, gallery.SortOrder);
        Assert.Equal(gallery.Id, nestedGallery.ParentGalleryEntityId);
        Assert.Equal(0, nestedGallery.SortOrder);

        Assert.Collection(
            persistence.UpsertedImages.OrderBy(image => image.FilePath, StringComparer.OrdinalIgnoreCase),
            image => {
                Assert.Equal("/media/images/Gallery/A secondGallery/b.png", image.FilePath);
                Assert.Equal(nestedGallery.Id, image.GalleryEntityId);
            },
            image => {
                Assert.Equal("/media/images/Gallery/A secondGallery/b2.png", image.FilePath);
                Assert.Equal(nestedGallery.Id, image.GalleryEntityId);
            },
            image => {
                Assert.Equal("/media/images/Gallery/a.png", image.FilePath);
                Assert.Equal(gallery.Id, image.GalleryEntityId);
            },
            image => {
                Assert.Equal("/media/images/Gallery/a2.png", image.FilePath);
                Assert.Equal(gallery.Id, image.GalleryEntityId);
            },
            image => {
                Assert.Equal("/media/images/root.png", image.FilePath);
                Assert.Null(image.GalleryEntityId);
            });
        Assert.Equal(["/media/images/root.png"], persistence.ValidLooseImagePaths);
        Assert.Equal(["/media/images/Gallery", "/media/images/Gallery/A secondGallery"], persistence.ValidGalleryPaths);
        Assert.Equal(["/media/images/Gallery/a.png", "/media/images/Gallery/a2.png"], persistence.ValidImagePathsByGalleryId[gallery.Id]);
        Assert.Equal(["/media/images/Gallery/A secondGallery/b.png", "/media/images/Gallery/A secondGallery/b2.png"], persistence.ValidImagePathsByGalleryId[nestedGallery.Id]);
        Assert.Equal(2, persistence.GalleryBatchCalls);
        Assert.Equal(1, persistence.ImageBatchCalls);
        Assert.Equal([root.Id], persistence.LastScannedRootIds);
    }

    [Fact]
    public async Task GalleryScanCollapsesSingleImageLeafIntoParentGallery() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/images",
            "Images",
            Enabled: true,
            Recursive: true,
            ScanVideos: false,
            ScanImages: true,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings
        };
        // "Set" is a real gallery (two images); "Solo" holds a single image and no nested gallery, so
        // it collapses and its lone image is reparented to "Set".
        var discovery = new RecordingFileDiscovery(
            directoryGroups: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
                ["/media/images/Set"] = ["/media/images/Set/cover.png", "/media/images/Set/p2.png"],
                ["/media/images/Set/Solo"] = ["/media/images/Set/Solo/only.png"]
            });
        var handler = new ScanGalleryJobHandler(
            NullLogger<ScanGalleryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = GalleryJob(root);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        var setGallery = Assert.Single(persistence.UpsertedGalleries);
        Assert.Equal("/media/images/Set", setGallery.FolderPath);
        Assert.DoesNotContain(persistence.UpsertedGalleries, gallery => gallery.FolderPath == "/media/images/Set/Solo");

        var solo = Assert.Single(persistence.UpsertedImages, image => image.FilePath == "/media/images/Set/Solo/only.png");
        Assert.Equal(setGallery.Id, solo.GalleryEntityId);
        Assert.Equal(2, solo.SortOrder);

        Assert.Equal(["/media/images/Set"], persistence.ValidGalleryPaths);
        Assert.Equal(
            ["/media/images/Set/cover.png", "/media/images/Set/p2.png", "/media/images/Set/Solo/only.png"],
            persistence.ValidImagePathsByGalleryId[setGallery.Id]);
        Assert.Empty(persistence.ValidLooseImagePaths);
    }

    [Fact]
    public async Task GalleryScanCollapsesSingleImageFolderUnderRootIntoLooseImage() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/images",
            "Images",
            Enabled: true,
            Recursive: true,
            ScanVideos: false,
            ScanImages: true,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings
        };
        // "Solo" is directly under the root and holds a single image with no surviving ancestor
        // gallery, so the image becomes loose.
        var discovery = new RecordingFileDiscovery(
            directoryGroups: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
                ["/media/images/Solo"] = ["/media/images/Solo/only.png"]
            });
        var handler = new ScanGalleryJobHandler(
            NullLogger<ScanGalleryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = GalleryJob(root);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        Assert.Empty(persistence.UpsertedGalleries);
        Assert.Empty(persistence.ValidGalleryPaths);
        var solo = Assert.Single(persistence.UpsertedImages);
        Assert.Equal("/media/images/Solo/only.png", solo.FilePath);
        Assert.Null(solo.GalleryEntityId);
        Assert.Equal(["/media/images/Solo/only.png"], persistence.ValidLooseImagePaths);
    }

    [Fact]
    public async Task GalleryScanDoesNotCollapseSingleImageFolderWithChildGallery() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/images",
            "Images",
            Enabled: true,
            Recursive: true,
            ScanVideos: false,
            ScanImages: true,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings
        };
        // "Set" holds one image but also a child folder with images, so it is not a leaf and must
        // remain a gallery rather than collapsing its single direct image.
        var discovery = new RecordingFileDiscovery(
            directoryGroups: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
                ["/media/images/Set"] = ["/media/images/Set/one.png"],
                ["/media/images/Set/Sub"] = ["/media/images/Set/Sub/leaf.png", "/media/images/Set/Sub/leaf2.png"]
            });
        var handler = new ScanGalleryJobHandler(
            NullLogger<ScanGalleryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = GalleryJob(root);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        var setGallery = Assert.Single(persistence.UpsertedGalleries, gallery => gallery.FolderPath == "/media/images/Set");
        var subGallery = Assert.Single(persistence.UpsertedGalleries, gallery => gallery.FolderPath == "/media/images/Set/Sub");
        Assert.Equal(setGallery.Id, subGallery.ParentGalleryEntityId);
        Assert.Equal(["/media/images/Set", "/media/images/Set/Sub"], persistence.ValidGalleryPaths);

        var directImage = Assert.Single(persistence.UpsertedImages, image => image.FilePath == "/media/images/Set/one.png");
        Assert.Equal(setGallery.Id, directImage.GalleryEntityId);
        Assert.Equal(["/media/images/Set/one.png"], persistence.ValidImagePathsByGalleryId[setGallery.Id]);
        Assert.Equal(
            ["/media/images/Set/Sub/leaf.png", "/media/images/Set/Sub/leaf2.png"],
            persistence.ValidImagePathsByGalleryId[subGallery.Id]);
    }

    private static JobRunSnapshot GalleryJob(LibraryRootData root) => new(
        Guid.NewGuid(),
        JobType.ScanGallery,
        JobRunStatus.Running,
        Progress: 0,
        Message: null,
        PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
        TargetEntityKind: "library-root",
        TargetEntityId: root.Id.ToString(),
        TargetLabel: root.Label,
        CreatedAt: DateTimeOffset.UtcNow,
        StartedAt: DateTimeOffset.UtcNow,
        FinishedAt: null);

    [Fact]
    public async Task AudioScanTreatsRootTracksAsLooseAndFoldersAsNestedLibraries() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/audio",
            "Audio",
            Enabled: true,
            Recursive: true,
            ScanVideos: false,
            ScanImages: false,
            ScanAudio: true,
            ScanBooks: false,
            IsNsfw: false);
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings
        };
        var discovery = new RecordingFileDiscovery(
            directoryGroups: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
                ["/media/audio"] = ["/media/audio/root.flac"],
                ["/media/audio/Album"] = ["/media/audio/Album/one.flac"],
                ["/media/audio/Album/Disc 2"] = ["/media/audio/Album/Disc 2/two.flac"]
            });
        var handler = new ScanAudioJobHandler(
            NullLogger<ScanAudioJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanAudio,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        // "Album" holds tracks directly, so it is a single album; its "Disc 2" subfolder becomes a
        // section of that album rather than a nested library. No artist folder exists here.
        Assert.DoesNotContain(persistence.UpsertedAudioLibraries, library => library.FolderPath == root.Path);
        Assert.DoesNotContain(persistence.UpsertedAudioLibraries, item => item.FolderPath == "/media/audio/Album/Disc 2");
        Assert.Empty(persistence.UpsertedMusicArtists);
        var album = Assert.Single(persistence.UpsertedAudioLibraries, item => item.FolderPath == "/media/audio/Album");
        Assert.Null(album.ParentAudioLibraryEntityId);
        Assert.Equal(0, album.SortOrder);

        Assert.Collection(
            persistence.UpsertedAudioTracks.OrderBy(track => track.FilePath, StringComparer.OrdinalIgnoreCase),
            track => {
                Assert.Equal("/media/audio/Album/Disc 2/two.flac", track.FilePath);
                Assert.Equal(album.Id, track.AudioLibraryEntityId);
                Assert.Equal("Disc 2", track.SectionLabel);
                Assert.Equal(1, track.SectionOrder);
            },
            track => {
                Assert.Equal("/media/audio/Album/one.flac", track.FilePath);
                Assert.Equal(album.Id, track.AudioLibraryEntityId);
                Assert.Null(track.SectionLabel);
                Assert.Equal(0, track.SectionOrder);
            },
            track => {
                Assert.Equal("/media/audio/root.flac", track.FilePath);
                Assert.Null(track.AudioLibraryEntityId);
            });
        Assert.Equal(["/media/audio/root.flac"], persistence.ValidLooseAudioTrackPaths);
        Assert.Equal(["/media/audio/Album"], persistence.ValidAudioLibraryPaths);
        Assert.Equal(
            ["/media/audio/Album/Disc 2/two.flac", "/media/audio/Album/one.flac"],
            persistence.ValidAudioTrackPathsByLibraryId[album.Id].OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(1, persistence.AudioLibraryBatchCalls);
        Assert.Equal(1, persistence.AudioTrackBatchCalls);
        Assert.Equal([root.Id], persistence.LastScannedRootIds);
    }

    [Fact]
    public async Task AudioScanTreatsSingleLayerFolderAsArtistlessAlbum() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/audio",
            "Audio",
            Enabled: true,
            Recursive: true,
            ScanVideos: false,
            ScanImages: false,
            ScanAudio: true,
            ScanBooks: false,
            IsNsfw: false);
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings
        };
        var discovery = new RecordingFileDiscovery(
            directoryGroups: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
                ["/media/audio/The Album"] = [
                    "/media/audio/The Album/01 First.flac",
                    "/media/audio/The Album/02 Second.flac"
                ]
            });
        var handler = new ScanAudioJobHandler(
            NullLogger<ScanAudioJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanAudio,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

        Assert.Empty(persistence.UpsertedMusicArtists);
        var album = Assert.Single(persistence.UpsertedAudioLibraries);
        Assert.Equal("/media/audio/The Album", album.FolderPath);
        Assert.Equal("The Album", album.Title);
        Assert.Null(album.ParentAudioLibraryEntityId);

        Assert.Collection(
            persistence.UpsertedAudioTracks.OrderBy(track => track.SortOrder),
            track => {
                Assert.Equal("/media/audio/The Album/01 First.flac", track.FilePath);
                Assert.Equal(album.Id, track.AudioLibraryEntityId);
                Assert.Null(track.SectionLabel);
            },
            track => {
                Assert.Equal("/media/audio/The Album/02 Second.flac", track.FilePath);
                Assert.Equal(album.Id, track.AudioLibraryEntityId);
                Assert.Null(track.SectionLabel);
            });
        Assert.Equal(["/media/audio/The Album"], persistence.ValidAudioLibraryPaths);
        Assert.Empty(persistence.ValidMusicArtistPaths);
    }

    [Fact]
    public async Task BookScanMaterializesFolderVolumesChaptersAndPages() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-book-scan-");
        try {
            var rootPath = tempRoot.FullName;
            var volumePath = Path.Combine(rootPath, "Promised Neverland", "Volume 01");
            Directory.CreateDirectory(volumePath);
            var chapterOnePath = Path.Combine(volumePath, "Promised Neverland Ch.1.zip");
            var chapterTwoPath = Path.Combine(volumePath, "Promised Neverland Ch.2.zip");
            CreateZip(chapterOnePath, ["002.jpg", "001.jpg"]);
            CreateZip(chapterTwoPath, ["001.jpg"]);

            var root = new LibraryRootData(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                rootPath,
                "Comics",
                Enabled: true,
                Recursive: true,
                ScanVideos: false,
                ScanImages: false,
                ScanAudio: false,
                ScanBooks: true,
                IsNsfw: false);
            var persistence = new FakeScanPersistence([root]) {
                Settings = new LibrarySettingsData(
                    AutoGenerateMetadata: false,
                    AutoGenerateOshash: false,
                    AutoGenerateMd5: false,
                    AutoGeneratePreview: false,
                    GenerateTrickplay: false,
                    TrickplayIntervalSeconds: 10,
                    PreviewClipDurationSeconds: 8,
                    ThumbnailQuality: 2,
                    TrickplayQuality: 2)
            };
            var handler = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([chapterTwoPath, chapterOnePath]),
                persistence,
                persistence,
                persistence);
            var job = new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.ScanBook,
                JobRunStatus.Running,
                Progress: 0,
                Message: null,
                PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
                TargetEntityKind: "library-root",
                TargetEntityId: root.Id.ToString(),
                TargetLabel: root.Label,
                CreatedAt: DateTimeOffset.UtcNow,
                StartedAt: DateTimeOffset.UtcNow,
                FinishedAt: null);

            await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

            var book = Assert.Single(persistence.UpsertedBooks);
            Assert.Equal(Path.Combine(rootPath, "Promised Neverland"), book.SourcePath);
            Assert.Equal("Promised Neverland", book.Title);

            var volume = Assert.Single(persistence.UpsertedBookVolumes);
            Assert.Equal(volumePath, volume.SourcePath);
            Assert.Equal("Volume 01", volume.Title);
            Assert.Equal(book.Id, volume.BookEntityId);
            Assert.Equal(0, volume.SortOrder);

            Assert.Collection(
                persistence.UpsertedBookChapters,
                chapter => {
                    Assert.Equal(chapterOnePath, chapter.SourcePath);
                    Assert.Equal("Promised Neverland Ch.1", chapter.Title);
                    Assert.Equal(volume.Id, chapter.ParentEntityId);
                    Assert.Equal(0, chapter.SortOrder);
                    Assert.Equal(2, chapter.PageCount);
                },
                chapter => {
                    Assert.Equal(chapterTwoPath, chapter.SourcePath);
                    Assert.Equal("Promised Neverland Ch.2", chapter.Title);
                    Assert.Equal(volume.Id, chapter.ParentEntityId);
                    Assert.Equal(1, chapter.SortOrder);
                    Assert.Equal(1, chapter.PageCount);
                });

            Assert.Equal(
                [
                    $"{chapterOnePath}::001.jpg",
                    $"{chapterOnePath}::002.jpg",
                    $"{chapterTwoPath}::001.jpg"
                ],
                persistence.UpsertedBookPages.Select(page => page.SourcePath).ToArray());
            Assert.All(persistence.UpsertedBookPages, page => Assert.Equal(book.Id, page.BookEntityId));
            Assert.Equal(2, persistence.BookPageBatchCalls);
            Assert.Equal([Path.Combine(rootPath, "Promised Neverland")], persistence.ValidBookPaths);
            Assert.Equal([volumePath], persistence.ValidBookVolumePaths);
            Assert.Equal([chapterOnePath, chapterTwoPath], persistence.ValidBookChapterPaths);
            Assert.Equal([root.Id], persistence.LastScannedRootIds);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BookScanSkipsAlreadyOrganizedAutoIdentifyRootsWhenUnorganizedOnly() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-book-auto-skip-");
        try {
            var rootPath = tempRoot.FullName;
            var bookPath = Path.Combine(rootPath, "Sonic X");
            Directory.CreateDirectory(bookPath);
            var archivePath = Path.Combine(bookPath, "chapter-001.cbz");
            CreateZip(archivePath, ["001.jpg"]);

            var root = new LibraryRootData(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                rootPath,
                "Comics",
                Enabled: true,
                Recursive: true,
                ScanVideos: false,
                ScanImages: false,
                ScanAudio: false,
                ScanBooks: true,
                IsNsfw: false);
            var persistence = new FakeScanPersistence([root]) {
                Settings = new LibrarySettingsData(
                    AutoGenerateMetadata: false,
                    AutoGenerateOshash: false,
                    AutoGenerateMd5: false,
                    AutoGeneratePreview: false,
                    GenerateTrickplay: false,
                    TrickplayIntervalSeconds: 10,
                    PreviewClipDurationSeconds: 8,
                    ThumbnailQuality: 2,
                    TrickplayQuality: 2,
                    AutoIdentifyEnabled: true,
                    AutoIdentifyKinds: ["book"],
                    AutoIdentifyUnorganizedOnly: true),
                OrganizedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { bookPath }
            };
            var queue = new RecordingJobQueue();
            var handler = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([archivePath]),
                persistence,
                persistence,
                persistence);
            var job = new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.ScanBook,
                JobRunStatus.Running,
                Progress: 0,
                Message: null,
                PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
                TargetEntityKind: "library-root",
                TargetEntityId: root.Id.ToString(),
                TargetLabel: root.Label,
                CreatedAt: DateTimeOffset.UtcNow,
                StartedAt: DateTimeOffset.UtcNow,
                FinishedAt: null);

            await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);

            Assert.DoesNotContain(queue.Enqueued, request => request.Type == JobType.AutoIdentify);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BookScanReadsComicInfoForTitlesAndMetadata() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-comicinfo-scan-");
        try {
            var rootPath = tempRoot.FullName;
            var bookPath = Path.Combine(rootPath, "Filename Series");
            Directory.CreateDirectory(bookPath);
            var archivePath = Path.Combine(bookPath, "chapter-001.cbz");
            var metadata = new ComicInfoMetadata {
                Series = "Metadata Series",
                Title = "Metadata Chapter",
                Summary = "Comic summary",
                Publisher = "Metadata Publisher",
                Tags = ["Drama"],
                Creators = ["Ada Writer"],
                Date = "2026-05",
                MarksNsfw = true
            };
            CreateZip(archivePath, ["001.jpg"]);

            var root = new LibraryRootData(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                rootPath,
                "Comics",
                Enabled: true,
                Recursive: true,
                ScanVideos: false,
                ScanImages: false,
                ScanAudio: false,
                ScanBooks: true,
                IsNsfw: false);
            var persistence = new FakeScanPersistence([root]) {
                Settings = DisabledGeneratedWorkSettings
            };
            var metadataPersistence = new RecordingScanMetadataPersistence();
            var handler = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([archivePath]),
                persistence,
                persistence,
                persistence,
                comicInfoReader: new StubComicInfoMetadataReader(metadata),
                scanMetadata: metadataPersistence);
            var job = new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.ScanBook,
                JobRunStatus.Running,
                Progress: 0,
                Message: null,
                PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
                TargetEntityKind: "library-root",
                TargetEntityId: root.Id.ToString(),
                TargetLabel: root.Label,
                CreatedAt: DateTimeOffset.UtcNow,
                StartedAt: DateTimeOffset.UtcNow,
                FinishedAt: null);

            await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

            var book = Assert.Single(persistence.UpsertedBooks);
            Assert.Equal("Metadata Series", book.Title);
            var chapter = Assert.Single(persistence.UpsertedBookChapters);
            Assert.Equal("Metadata Chapter", chapter.Title);
            var applied = Assert.Single(metadataPersistence.AppliedComics);
            Assert.Equal(book.Id, applied.EntityId);
            Assert.Same(metadata, applied.Metadata);
            Assert.True(applied.MarkNsfw);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileBookScanMaterializesFolderAsBookAuthorWithChildBooks() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-single-book-series-scan-");
        try {
            var rootPath = tempRoot.FullName;
            var seriesPath = Path.Combine(rootPath, "Game of Thrones");
            Directory.CreateDirectory(seriesPath);
            var firstBookPath = Path.Combine(seriesPath, "01 - A Game of Thrones.pdf");
            var secondBookPath = Path.Combine(seriesPath, "02 - A Clash of Kings.epub");
            await File.WriteAllTextAsync(firstBookPath, "pdf");
            await File.WriteAllTextAsync(secondBookPath, "epub");

            var root = new LibraryRootData(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                rootPath,
                "Books",
                Enabled: true,
                Recursive: true,
                ScanVideos: false,
                ScanImages: false,
                ScanAudio: false,
                ScanBooks: true,
                IsNsfw: false);
            var persistence = new FakeScanPersistence([root]) {
                Settings = DisabledGeneratedWorkSettings
            };
            var handler = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([secondBookPath, firstBookPath]),
                persistence,
                persistence,
                persistence);
            var job = new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.ScanBook,
                JobRunStatus.Running,
                Progress: 0,
                Message: null,
                PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
                TargetEntityKind: "library-root",
                TargetEntityId: root.Id.ToString(),
                TargetLabel: root.Label,
                CreatedAt: DateTimeOffset.UtcNow,
                StartedAt: DateTimeOffset.UtcNow,
                FinishedAt: null);

            await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

            // The top-level folder is grouped as a book author (mirroring Artist/Album), not a series.
            var author = Assert.Single(persistence.UpsertedBookAuthors);
            Assert.Equal(seriesPath, author.FolderPath);
            Assert.Equal("Game of Thrones", author.Title);
            Assert.Empty(persistence.UpsertedBookSeries);

            Assert.Collection(
                persistence.UpsertedBooks,
                book => {
                    Assert.Equal(firstBookPath, book.SourcePath);
                    Assert.Equal("01 - A Game of Thrones", book.Title);
                    Assert.Equal(author.Id, book.ParentEntityId);
                    Assert.Equal(0, book.SortOrder);
                },
                book => {
                    Assert.Equal(secondBookPath, book.SourcePath);
                    Assert.Equal("02 - A Clash of Kings", book.Title);
                    Assert.Equal(author.Id, book.ParentEntityId);
                    Assert.Equal(1, book.SortOrder);
                });
            // The author folder is not a book path; only the book files are tracked for stale cleanup.
            Assert.Equal([firstBookPath, secondBookPath], persistence.ValidBookPaths);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileBookScanNamesAuthorFromEmbeddedCreatorNotFolder() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-book-author-metadata-");
        try {
            var rootPath = tempRoot.FullName;
            var seriesPath = Path.Combine(rootPath, "Game of Thrones");
            Directory.CreateDirectory(seriesPath);
            var bookPath = Path.Combine(seriesPath, "01 - A Game of Thrones.epub");
            await File.WriteAllTextAsync(bookPath, "epub");

            var root = new LibraryRootData(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                rootPath, "Books", Enabled: true, Recursive: true,
                ScanVideos: false, ScanImages: false, ScanAudio: false, ScanBooks: true, IsNsfw: false);
            var persistence = new FakeScanPersistence([root]) { Settings = DisabledGeneratedWorkSettings };
            var handler = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([bookPath]),
                persistence, persistence, persistence,
                bookFileMetadata: new StubBookFileMetadataReader(new ComicInfoMetadata {
                    Title = "A Game of Thrones",
                    Creators = ["George R.R. Martin"],
                }));
            var job = new JobRunSnapshot(
                Guid.NewGuid(), JobType.ScanBook, JobRunStatus.Running, 0, null,
                $$"""{"libraryRootId":"{{root.Id}}"}""",
                "library-root", root.Id.ToString(), root.Label,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);

            await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

            // The series-named folder ("Game of Thrones") must NOT become the author — the embedded
            // EPUB creator wins, so the author is named "George R.R. Martin".
            var author = Assert.Single(persistence.UpsertedBookAuthors);
            Assert.Equal("George R.R. Martin", author.Title);
            Assert.Equal(seriesPath, author.FolderPath);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    private sealed class StubBookFileMetadataReader(ComicInfoMetadata metadata) : IBookFileMetadataReader {
        public Task<ComicInfoMetadata?> ReadAsync(string sourcePath, BookFormat format, CancellationToken cancellationToken) =>
            Task.FromResult<ComicInfoMetadata?>(metadata);
    }

    [Fact]
    public async Task HandlesScheduledLibraryRootPayloadAsSingleRootScan() {
        var targetRoot = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/one",
            "Root One",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var otherRoot = targetRoot with {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Path = "/media/two",
            Label = "Root Two"
        };
        var persistence = new FakeScanPersistence([targetRoot, otherRoot]);
        var handler = new RecordingScanHandler(persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{targetRoot.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: targetRoot.Id.ToString(),
            TargetLabel: targetRoot.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None);

        Assert.Equal([targetRoot.Id], handler.ScannedRootIds);
        Assert.Equal(targetRoot.Id, persistence.LoadedRootIds.Single());
        Assert.False(persistence.LoadedEnabledRoots);
        Assert.Equal([targetRoot.Id], persistence.LastScannedRootIds);
    }

    [Fact]
    public async Task AllRootsScanProgressReportsCountWithoutLeakingLibraryNames() {
        // The all-roots scan job (no root payload) is not scoped to a single target, so the jobs list
        // cannot redact it for SFW viewers. Its progress message must therefore never name a library —
        // otherwise an NSFW library name leaks into the dashboard even in SFW mode (APP-125).
        var sfwRoot = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/one",
            "Family Movies",
            Enabled: true,
            Recursive: true,
            ScanVideos: true,
            ScanImages: false,
            ScanAudio: false,
            ScanBooks: false,
            IsNsfw: false);
        var nsfwRoot = sfwRoot with {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Path = "/media/secret",
            Label = "Secret Adult Stash",
            IsNsfw = true
        };
        var persistence = new FakeScanPersistence([sfwRoot, nsfwRoot]);
        var handler = new RecordingScanHandler(persistence);
        var queue = new RecordingJobQueue();
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: "{}",
            TargetEntityKind: null,
            TargetEntityId: null,
            TargetLabel: null,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

        await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);

        Assert.Equal([sfwRoot.Id, nsfwRoot.Id], handler.ScannedRootIds);
        Assert.True(persistence.LoadedEnabledRoots);
        Assert.DoesNotContain(queue.ProgressMessages, message => message is not null && message.Contains(nsfwRoot.Label));
        Assert.DoesNotContain(queue.ProgressMessages, message => message is not null && message.Contains(sfwRoot.Label));
        Assert.Equal(
            ["Scanned 1 of 2 libraries", "Scanned 2 of 2 libraries"],
            queue.ProgressMessages);
    }

    [Fact]
    public async Task SnapshotSkipsDetailedScanWhenNoFilesChanged() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos", "Videos",
            Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        var persistence = new FakeScanPersistence([root]);
        var snapshots = new FakeScanSnapshotStore();
        var discovery = new RecordingFileDiscovery(["/media/videos/a.mkv", "/media/videos/b.mkv"]);
        var handler = new RecordingScanHandler(persistence, snapshots, discovery);
        var job = SingleRootScanJob(root);

        // First scan: no snapshot yet, so the detailed scan runs and records the snapshot.
        await handler.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None);
        Assert.Equal([root.Id], handler.ScannedRootIds);
        Assert.Equal(1, snapshots.ApplyCount);

        // Second scan with the identical file set: the detailed scan is skipped (no new root id).
        await handler.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None);
        Assert.Equal([root.Id], handler.ScannedRootIds);
        Assert.Equal(1, snapshots.ApplyCount);
    }

    [Fact]
    public async Task SnapshotRescansWhenAFileIsAdded() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos", "Videos",
            Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        var persistence = new FakeScanPersistence([root]);
        var snapshots = new FakeScanSnapshotStore();
        var job = SingleRootScanJob(root);

        // First scan sees one file and builds the snapshot.
        var first = new RecordingScanHandler(persistence, snapshots, new RecordingFileDiscovery(["/media/videos/a.mkv"]));
        await first.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None);
        Assert.Equal([root.Id], first.ScannedRootIds);

        // A later scan (sharing the snapshot store) sees an added file, so it must rescan.
        var second = new RecordingScanHandler(persistence, snapshots, new RecordingFileDiscovery(["/media/videos/a.mkv", "/media/videos/b.mkv"]));
        await second.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None);
        Assert.Equal([root.Id], second.ScannedRootIds);
        Assert.Equal(2, snapshots.ApplyCount);
    }

    [Fact]
    public async Task SnapshotNoChangeScanStillQueuesPendingAutoIdentifyRoots() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos", "Videos",
            Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var persistence = new FakeScanPersistence([root]) {
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: false,
                AutoGenerateOshash: false,
                AutoGenerateMd5: false,
                AutoGeneratePreview: false,
                GenerateTrickplay: false,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2,
                AutoIdentifyEnabled: true,
                AutoIdentifyKinds: ["video"]),
            UpsertedVideoIds = [videoId],
            AutoIdentifyRootTargets = [new AutoIdentifyRootTarget(videoId, "video", "movie.mkv")],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false, NeedsGridThumbnail: false)
            }
        };
        var snapshots = new FakeScanSnapshotStore();
        var discovery = new RecordingFileDiscovery(["/media/videos/movie.mkv"]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence,
            snapshots);
        var job = SingleRootScanJob(root);

        await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);
        var secondQueue = new RecordingJobQueue();

        await handler.HandleAsync(new JobContext(job, secondQueue), CancellationToken.None);

        var request = Assert.Single(secondQueue.Enqueued, request => request.Type == JobType.AutoIdentify);
        Assert.Equal(videoId.ToString(), request.TargetEntityId);
    }

    private static JobRunSnapshot SingleRootScanJob(LibraryRootData root) =>
        new(
            Guid.NewGuid(),
            JobType.ScanLibrary,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: $$"""{"libraryRootId":"{{root.Id}}"}""",
            TargetEntityKind: "library-root",
            TargetEntityId: root.Id.ToString(),
            TargetLabel: root.Label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

    private static void CreateZip(string path, IReadOnlyList<string> members) {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var member in members) {
            var entry = archive.CreateEntry(member);
            using var entryStream = entry.Open();
            entryStream.WriteByte(1);
        }
    }

    private static LibrarySettingsData DisabledGeneratedWorkSettings => new(
        AutoGenerateMetadata: false,
        AutoGenerateOshash: false,
        AutoGenerateMd5: false,
        AutoGeneratePreview: false,
        GenerateTrickplay: false,
        TrickplayIntervalSeconds: 10,
        PreviewClipDurationSeconds: 8,
        ThumbnailQuality: 2,
        TrickplayQuality: 2);

    private sealed class RecordingScanHandler(
        FakeScanPersistence persistence,
        IScanSnapshotStore? snapshots = null,
        IFileDiscovery? discovery = null)
        : ScanJobHandler(NullLogger<RecordingScanHandler>.Instance, discovery ?? new NoopFileDiscovery(), persistence, snapshots) {
        public List<Guid> ScannedRootIds { get; } = [];

        public override JobType Type => JobType.ScanLibrary;

        protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanVideos;

        protected override IReadOnlyList<MediaCategory> ScanCategories => [MediaCategory.Video];

        protected override Task ScanRootCoreAsync(
            JobContext context,
            LibraryRootData root,
            CancellationToken cancellationToken) {
            ScannedRootIds.Add(root.Id);
            return Task.CompletedTask;
        }
    }

    /// <summary>In-memory <see cref="IScanSnapshotStore"/> for exercising the incremental fast path.</summary>
    private sealed class FakeScanSnapshotStore : IScanSnapshotStore {
        private readonly Dictionary<(Guid Root, string Kind), Dictionary<string, FileSignature>> _store = new();

        public int ApplyCount { get; private set; }

        public void Seed(Guid rootId, string scanKind, IReadOnlyList<string> paths) {
            _store[(rootId, scanKind)] = paths.ToDictionary(
                path => path,
                path => new FileSignature(path, path.Length, 0),
                StringComparer.OrdinalIgnoreCase);
        }

        public Task<IReadOnlyList<FileSignature>> LoadAsync(Guid rootId, string scanKind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileSignature>>(
                _store.TryGetValue((rootId, scanKind), out var map) ? map.Values.ToArray() : []);

        public Task ApplyAsync(Guid rootId, string scanKind, ScanDelta delta, CancellationToken cancellationToken) {
            if (!delta.HasChanges) {
                return Task.CompletedTask;
            }

            ApplyCount++;
            if (!_store.TryGetValue((rootId, scanKind), out var map)) {
                map = new Dictionary<string, FileSignature>(StringComparer.OrdinalIgnoreCase);
                _store[(rootId, scanKind)] = map;
            }

            foreach (var added in delta.Added) map[added.Path] = added;
            foreach (var changed in delta.Changed) map[changed.Path] = changed;
            foreach (var removed in delta.Removed) map.Remove(removed.Path);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScanPersistence(IReadOnlyList<LibraryRootData> roots) : ILibraryScanRootPersistence, IVideoScanPersistence, IDownstreamNeedsPersistence, IImageGalleryScanPersistence, IAudioScanPersistence, IBookScanPersistence {
        public List<Guid> LoadedRootIds { get; } = [];
        public List<Guid> LastScannedRootIds { get; } = [];
        public bool LoadedEnabledRoots { get; private set; }
        public LibrarySettingsData Settings { get; init; } = new(
            AutoGenerateMetadata: true,
            AutoGenerateOshash: true,
            AutoGenerateMd5: true,
            AutoGeneratePreview: true,
            GenerateTrickplay: true,
            TrickplayIntervalSeconds: 10,
            PreviewClipDurationSeconds: 8,
            ThumbnailQuality: 2,
            TrickplayQuality: 2);
        public IReadOnlyList<Guid> UpsertedVideoIds { get; init; } = [];
        public bool HasTechnical { get; init; }
        public IReadOnlyList<EntityRefreshTarget> ExistingAudioTrackTargets { get; init; } = [];
        public IReadOnlyDictionary<Guid, DownstreamNeeds> DownstreamNeedsById { get; init; } =
            new Dictionary<Guid, DownstreamNeeds>();
        public IReadOnlyList<AutoIdentifyRootTarget>? AutoIdentifyRootTargets { get; init; }
        public IReadOnlySet<string> OrganizedSourcePaths { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<Guid> _organizedEntityIds = [];
        public List<VideoUpsertItem> UpsertedVideoItems { get; } = [];
        public List<ImageRecord> UpsertedImages { get; } = [];
        public List<GalleryRecord> UpsertedGalleries { get; } = [];
        public List<AudioTrackRecord> UpsertedAudioTracks { get; } = [];
        public List<AudioLibraryRecord> UpsertedAudioLibraries { get; } = [];
        public List<MusicArtistRecord> UpsertedMusicArtists { get; } = [];
        public List<BookRecord> UpsertedBooks { get; } = [];
        public List<BookSeriesRecord> UpsertedBookSeries { get; } = [];
        public List<BookVolumeRecord> UpsertedBookVolumes { get; } = [];
        public List<BookChapterRecord> UpsertedBookChapters { get; } = [];
        public List<BookPageRecord> UpsertedBookPages { get; } = [];
        public int GalleryBatchCalls { get; private set; }
        public int ImageBatchCalls { get; private set; }
        public int MusicArtistBatchCalls { get; private set; }
        public int AudioLibraryBatchCalls { get; private set; }
        public int AudioTrackBatchCalls { get; private set; }
        public int BookPageBatchCalls { get; private set; }
        public IReadOnlyList<string> ValidLooseImagePaths { get; private set; } = [];
        public IReadOnlyList<string> ValidMoviePaths { get; private set; } = [];
        public Dictionary<Guid, IReadOnlyList<string>> ValidImagePathsByGalleryId { get; } = [];
        public IReadOnlyList<string> ValidGalleryPaths { get; private set; } = [];
        public IReadOnlyList<string> ValidLooseAudioTrackPaths { get; private set; } = [];
        public Dictionary<Guid, IReadOnlyList<string>> ValidAudioTrackPathsByLibraryId { get; } = [];
        public IReadOnlyList<string> ValidAudioLibraryPaths { get; private set; } = [];
        public IReadOnlyList<string> ValidMusicArtistPaths { get; private set; } = [];
        public IReadOnlyList<string> ValidBookPaths { get; private set; } = [];
        public IReadOnlyList<string> ValidBookVolumePaths { get; private set; } = [];
        public IReadOnlyList<string> ValidBookChapterPaths { get; private set; } = [];
        public IReadOnlyDictionary<Guid, IReadOnlySet<string>> ExcludedPathsByRoot { get; init; } =
            new Dictionary<Guid, IReadOnlySet<string>>();
        public IReadOnlySet<Guid> DeletedRootIds { get; init; } = new HashSet<Guid>();
        private readonly Dictionary<string, Guid> _entityIdsBySource = new(StringComparer.OrdinalIgnoreCase);

        public Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken) {
            LoadedRootIds.Add(rootId);
            if (DeletedRootIds.Contains(rootId)) {
                return Task.FromResult<LibraryRootData?>(null);
            }

            return Task.FromResult(roots.FirstOrDefault(root => root.Id == rootId));
        }

        public Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken) {
            LoadedEnabledRoots = true;
            return Task.FromResult(roots);
        }

        public Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Settings);

        public Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken) {
            LastScannedRootIds.Add(rootId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlySet<string>> GetExcludedPathsForRootAsync(Guid rootId, CancellationToken cancellationToken) =>
            Task.FromResult(ExcludedPathsByRoot.GetValueOrDefault(rootId) ??
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) as IReadOnlySet<string>);

        public Task<int> RemoveEntitiesInExcludedPathsAsync(Guid rootId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> RemoveEntitiesOutsideLibraryRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<Guid> UpsertVideoAsync(string filePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid> UpsertImageAsync(string filePath, string title, Guid? galleryEntityId, long? sizeBytes, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"image:{filePath}");
            UpsertedImages.Add(new ImageRecord(id, filePath, title, galleryEntityId, sortOrder));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertGalleryAsync(string folderPath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) =>
            UpsertGalleryAsync(folderPath, title, libraryRootId, parentGalleryEntityId: null, sortOrder: 0, isNsfw, cancellationToken);

        public Task<Guid> UpsertGalleryAsync(string folderPath, string title, Guid libraryRootId, Guid? parentGalleryEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"gallery:{folderPath}");
            UpsertedGalleries.Add(new GalleryRecord(id, folderPath, title, libraryRootId, parentGalleryEntityId, sortOrder));
            return Task.FromResult(id);
        }

        public async Task<IReadOnlyList<Guid>> UpsertGalleriesBatchAsync(IReadOnlyList<GalleryUpsertItem> items, CancellationToken cancellationToken) {
            GalleryBatchCalls++;
            var ids = new List<Guid>(items.Count);
            foreach (var item in items) {
                ids.Add(await UpsertGalleryAsync(
                    item.FolderPath,
                    item.Title,
                    item.LibraryRootId,
                    item.ParentGalleryEntityId,
                    item.SortOrder,
                    item.IsNsfw,
                    cancellationToken));
            }

            return ids;
        }

        public Task<Guid> UpsertAudioTrackAsync(string filePath, string title, Guid? audioLibraryId, int sortOrder, string? sectionLabel, int sectionOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"audio-track:{filePath}");
            UpsertedAudioTracks.Add(new AudioTrackRecord(id, filePath, title, audioLibraryId, sortOrder, sectionLabel, sectionOrder));
            return Task.FromResult(id);
        }

        public async Task<IReadOnlyList<Guid>> UpsertImagesBatchAsync(IReadOnlyList<ImageUpsertItem> items, CancellationToken cancellationToken) {
            ImageBatchCalls++;
            var ids = new List<Guid>(items.Count);
            foreach (var item in items) {
                ids.Add(await UpsertImageAsync(
                    item.FilePath,
                    item.Title,
                    item.GalleryEntityId,
                    item.SizeBytes,
                    item.SortOrder,
                    item.IsNsfw,
                    cancellationToken));
            }

            return ids;
        }

        public Task<Guid> UpsertAudioLibraryAsync(string folderPath, string title, Guid libraryRootId, Guid? parentEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"audio-library:{folderPath}");
            UpsertedAudioLibraries.Add(new AudioLibraryRecord(id, folderPath, title, libraryRootId, parentEntityId, sortOrder));
            return Task.FromResult(id);
        }

        public async Task<IReadOnlyList<Guid>> UpsertAudioTracksBatchAsync(IReadOnlyList<AudioTrackUpsertItem> items, CancellationToken cancellationToken) {
            AudioTrackBatchCalls++;
            var ids = new List<Guid>(items.Count);
            foreach (var item in items) {
                ids.Add(await UpsertAudioTrackAsync(
                    item.FilePath,
                    item.Title,
                    item.AudioLibraryId,
                    item.SortOrder,
                    item.SectionLabel,
                    item.SectionOrder,
                    item.IsNsfw,
                    cancellationToken));
            }

            return ids;
        }

        public Task<IReadOnlyList<EntityRefreshTarget>> GetAudioTrackTargetsInRootAsync(
            Guid rootId, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingAudioTrackTargets);

        public async Task<IReadOnlyList<Guid>> UpsertAudioLibrariesBatchAsync(IReadOnlyList<AudioLibraryUpsertItem> items, CancellationToken cancellationToken) {
            AudioLibraryBatchCalls++;
            var ids = new List<Guid>(items.Count);
            foreach (var item in items) {
                ids.Add(await UpsertAudioLibraryAsync(
                    item.FolderPath,
                    item.Title,
                    item.LibraryRootId,
                    item.ParentEntityId,
                    item.SortOrder,
                    item.IsNsfw,
                    cancellationToken));
            }

            return ids;
        }

        public Task<Guid> UpsertMusicArtistAsync(string folderPath, string title, Guid libraryRootId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"music-artist:{folderPath}");
            UpsertedMusicArtists.Add(new MusicArtistRecord(id, folderPath, title, libraryRootId, sortOrder));
            return Task.FromResult(id);
        }

        public async Task<IReadOnlyList<Guid>> UpsertMusicArtistsBatchAsync(IReadOnlyList<MusicArtistUpsertItem> items, CancellationToken cancellationToken) {
            MusicArtistBatchCalls++;
            var ids = new List<Guid>(items.Count);
            foreach (var item in items) {
                ids.Add(await UpsertMusicArtistAsync(
                    item.FolderPath,
                    item.Title,
                    item.LibraryRootId,
                    item.SortOrder,
                    item.IsNsfw,
                    cancellationToken));
            }

            return ids;
        }

        public Task<Guid> UpsertBookAsync(string sourcePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"book:{sourcePath}");
            MarkOrganizedIfNeeded(id, sourcePath);
            UpsertedBooks.Add(new BookRecord(id, sourcePath, title, libraryRootId, null, null));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertBookSeriesAsync(string folderPath, string title, Guid libraryRootId, bool isNsfw, BookType bookType, BookFormat format, CancellationToken cancellationToken) {
            var id = IdFor($"book-series:{folderPath}");
            UpsertedBookSeries.Add(new BookSeriesRecord(id, folderPath, title, libraryRootId, bookType, format));
            return Task.FromResult(id);
        }

        public List<(Guid Id, string FolderPath, string Title)> UpsertedBookAuthors { get; } = [];

        public Task<Guid> UpsertBookAuthorAsync(string folderPath, string title, int? sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"book-author:{folderPath}");
            UpsertedBookAuthors.Add((id, folderPath, title));
            return Task.FromResult(id);
        }

        public Task<int> RemoveEmptyBookAuthorsAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<Guid> UpsertSingleFileBookAsync(
            string sourcePath,
            string title,
            Guid libraryRootId,
            bool isNsfw,
            BookType bookType,
            BookFormat format,
            string contentType,
            Guid? parentBookEntityId,
            int? sortOrder,
            CancellationToken cancellationToken) {
            var id = IdFor($"book:{sourcePath}");
            MarkOrganizedIfNeeded(id, sourcePath);
            UpsertedBooks.Add(new BookRecord(id, sourcePath, title, libraryRootId, parentBookEntityId, sortOrder));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertBookVolumeAsync(string folderPath, string title, Guid bookEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"book-volume:{folderPath}");
            UpsertedBookVolumes.Add(new BookVolumeRecord(id, folderPath, title, bookEntityId, sortOrder));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertBookChapterAsync(string archivePath, string title, Guid parentEntityId, int sortOrder, int pageCount, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"book-chapter:{archivePath}");
            UpsertedBookChapters.Add(new BookChapterRecord(id, archivePath, title, parentEntityId, sortOrder, pageCount));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertBookPageAsync(string filePath, string title, Guid bookEntityId, Guid chapterEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"book-page:{filePath}");
            UpsertedBookPages.Add(new BookPageRecord(id, filePath, title, bookEntityId, chapterEntityId, sortOrder));
            return Task.FromResult(id);
        }

        public async Task<IReadOnlyList<Guid>> UpsertBookPagesBatchAsync(IReadOnlyList<BookPageUpsertItem> items, CancellationToken cancellationToken) {
            BookPageBatchCalls++;
            var ids = new List<Guid>(items.Count);
            foreach (var item in items) {
                ids.Add(await UpsertBookPageAsync(
                    item.FilePath,
                    item.Title,
                    item.BookEntityId,
                    item.ChapterEntityId,
                    item.SortOrder,
                    item.IsNsfw,
                    cancellationToken));
            }

            return ids;
        }

        public Task<int> RemoveStaleVideosByRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> RemoveStaleMoviesByRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
            ValidMoviePaths = validFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleLooseImagesInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
            ValidLooseImagePaths = validPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleImagesInGalleryAsync(Guid galleryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
            ValidImagePathsByGalleryId[galleryEntityId] = validPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleGalleriesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
            ValidGalleryPaths = validFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleLooseAudioTracksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
            ValidLooseAudioTrackPaths = validPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleAudioTracksInLibraryAsync(Guid libraryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
            ValidAudioTrackPathsByLibraryId[libraryEntityId] = validPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleAudioLibrariesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
            ValidAudioLibraryPaths = validFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleMusicArtistsInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
            ValidMusicArtistPaths = validFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleBookVolumesAsync(Guid bookEntityId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) {
            ValidBookVolumePaths = validFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleBookChaptersAsync(Guid bookEntityId, IReadOnlySet<string> validArchivePaths, CancellationToken cancellationToken) {
            ValidBookChapterPaths = ValidBookChapterPaths
                .Concat(validArchivePaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveStaleBooksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) {
            ValidBookPaths = validPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveOrphanSeriesAndSeasonsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public int RemoveOrphanTagsCalls { get; private set; }

        public Task<int> RemoveOrphanTagsAsync(CancellationToken cancellationToken) {
            RemoveOrphanTagsCalls++;
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<Guid>> UpsertVideosBatchAsync(IReadOnlyList<VideoUpsertItem> items, CancellationToken cancellationToken) {
            UpsertedVideoItems.AddRange(items);
            return Task.FromResult(UpsertedVideoIds);
        }

        public Task<IReadOnlyDictionary<Guid, DownstreamNeeds>> CheckDownstreamNeedsBatchAsync(IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken) =>
            Task.FromResult(DownstreamNeedsById);

        // The fake models a flat video library unless a test supplies explicit root metadata.
        public Task<IReadOnlyList<AutoIdentifyRootTarget>> ResolveAutoIdentifyRootsAsync(IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutoIdentifyRootTarget>>(
                AutoIdentifyRootTargets ?? entityIds.Distinct().Select(id => new AutoIdentifyRootTarget(id, "video", "video.mkv")).ToList());

        public Task<IReadOnlyList<AutoIdentifyRootTarget>> ResolveAutoIdentifyRootsForLibraryRootAsync(
            Guid libraryRootId,
            IReadOnlyList<MediaCategory> scanCategories,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutoIdentifyRootTarget>>(AutoIdentifyRootTargets ?? []);

        public Task<bool> HasEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(HasTechnical);

        public Task<bool> HasEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> HasEntityFileAsync(Guid entityId, EntityFileRole role, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> IsEntityOrganizedAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(_organizedEntityIds.Contains(entityId));

        public Task<bool> HasSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertEntityTechnicalAsync(Guid entityId, double? duration, int? width, int? height, double? frameRate, int? bitRate, int? sampleRate, int? channels, string? codec, string? container, string? format, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertMediaSourceAsync(Guid entityId, string path, MediaSourceProbeData source, IReadOnlyList<MediaStreamProbeData> streams, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertTrickplayInfoAsync(Guid entityId, TrickplayInfoData info, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertEntityFileAsync(Guid entityId, EntityFileRole role, string path, string? mimeType, long? sizeBytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, string value, Guid? entityFileId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid?> GetSourceFileIdAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> GetSourceFilePathAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertSubtitleAsync(Guid entityId, string language, string? label, string format, EntitySubtitleSource source, string storagePath, string sourceFormat, int streamIndex, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertAudioTrackTagsAsync(Guid entityId, string? artist, string? album, int? trackNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EntityTechnicalData?> GetEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EntityRefreshTarget>> GetEntityTreeAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private void MarkOrganizedIfNeeded(Guid id, string sourcePath) {
            if (OrganizedSourcePaths.Contains(sourcePath)) {
                _organizedEntityIds.Add(id);
            }
        }

        private Guid IdFor(string key) {
            if (_entityIdsBySource.TryGetValue(key, out var id)) {
                return id;
            }

            id = Guid.NewGuid();
            _entityIdsBySource[key] = id;
            return id;
        }
    }

    private sealed record ImageRecord(Guid Id, string FilePath, string Title, Guid? GalleryEntityId, int SortOrder);
    private sealed record GalleryRecord(Guid Id, string FolderPath, string Title, Guid LibraryRootId, Guid? ParentGalleryEntityId, int SortOrder);
    private sealed record AudioTrackRecord(Guid Id, string FilePath, string Title, Guid? AudioLibraryEntityId, int SortOrder, string? SectionLabel, int SectionOrder);
    private sealed record AudioLibraryRecord(Guid Id, string FolderPath, string Title, Guid LibraryRootId, Guid? ParentAudioLibraryEntityId, int SortOrder);
    private sealed record MusicArtistRecord(Guid Id, string FolderPath, string Title, Guid LibraryRootId, int SortOrder);
    private sealed record BookRecord(Guid Id, string SourcePath, string Title, Guid LibraryRootId, Guid? ParentEntityId, int? SortOrder);
    private sealed record BookSeriesRecord(Guid Id, string SourcePath, string Title, Guid LibraryRootId, BookType BookType, BookFormat Format);
    private sealed record BookVolumeRecord(Guid Id, string SourcePath, string Title, Guid BookEntityId, int SortOrder);
    private sealed record BookChapterRecord(Guid Id, string SourcePath, string Title, Guid ParentEntityId, int SortOrder, int PageCount);
    private sealed record BookPageRecord(Guid Id, string SourcePath, string Title, Guid BookEntityId, Guid ChapterEntityId, int SortOrder);

    private sealed class StubVideoSidecarMetadataReader(VideoSidecarMetadata? metadata) : IVideoSidecarMetadataReader {
        public Task<VideoSidecarMetadata?> ReadAsync(string videoFilePath, CancellationToken cancellationToken) =>
            Task.FromResult(metadata);
    }

    private sealed class StubComicInfoMetadataReader(ComicInfoMetadata? metadata) : IComicInfoMetadataReader {
        public Task<ComicInfoMetadata?> ReadAsync(string archivePath, CancellationToken cancellationToken) =>
            Task.FromResult(metadata);
    }

    private sealed class RecordingScanMetadataPersistence : IScanMetadataPersistence {
        public List<AppliedVideoMetadata> AppliedVideos { get; } = [];
        public List<AppliedComicInfoMetadata> AppliedComics { get; } = [];

        public Task ApplyVideoSidecarMetadataAsync(
            Guid entityId,
            VideoSidecarMetadata metadata,
            string fallbackTitle,
            bool markNsfw,
            CancellationToken cancellationToken) {
            AppliedVideos.Add(new AppliedVideoMetadata(entityId, metadata, fallbackTitle, markNsfw));
            return Task.CompletedTask;
        }

        public Task ApplyComicInfoMetadataAsync(
            Guid bookEntityId,
            ComicInfoMetadata metadata,
            bool markNsfw,
            CancellationToken cancellationToken) {
            AppliedComics.Add(new AppliedComicInfoMetadata(bookEntityId, metadata, markNsfw));
            return Task.CompletedTask;
        }
    }

    private sealed record AppliedVideoMetadata(
        Guid EntityId,
        VideoSidecarMetadata Metadata,
        string FallbackTitle,
        bool MarkNsfw);

    private sealed record AppliedComicInfoMetadata(
        Guid EntityId,
        ComicInfoMetadata Metadata,
        bool MarkNsfw);

    private sealed class NoopFileDiscovery : IFileDiscovery {
        public Task<IReadOnlyList<string>> DiscoverFilesAsync(string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FileSignature>> DiscoverFileSignaturesAsync(string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileSignature>>([]);
    }

    private sealed class RecordingFileDiscovery(
        IReadOnlyList<string>? files = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? directoryGroups = null) : IFileDiscovery {
        public IReadOnlyList<string> LastExcludedPaths { get; private set; } = [];

        public Task<IReadOnlyList<string>> DiscoverFilesAsync(
            string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) {
            LastExcludedPaths = excludedPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            return Task.FromResult(files ?? []);
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(
            string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) {
            LastExcludedPaths = excludedPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (directoryGroups is not null) {
                return Task.FromResult(directoryGroups);
            }

            var grouped = (files ?? [])
                .GroupBy(path => Path.GetDirectoryName(path)!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(grouped);
        }

        public Task<IReadOnlyList<FileSignature>> DiscoverFileSignaturesAsync(
            string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) {
            LastExcludedPaths = excludedPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            var all = directoryGroups is not null
                ? directoryGroups.Values.SelectMany(group => group)
                : files ?? [];
            // Deterministic signatures so a re-run with the same inputs produces an identical snapshot.
            var signatures = all
                .Select(path => new FileSignature(path, path.Length, 0))
                .ToArray();
            return Task.FromResult<IReadOnlyList<FileSignature>>(signatures);
        }
    }

    private sealed class NoopJobQueue : IJobQueueService {
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) => Task.FromResult<JobRunSnapshot?>(null);
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobQueueCount>>([]);
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];
        public List<string?> ProgressMessages { get; } = [];

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Enqueued.Add(request);
            return Task.FromResult(new JobRunSnapshot(
                Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null,
                request.PayloadJson ?? "{}", request.TargetEntityKind, request.TargetEntityId, request.TargetLabel,
                DateTimeOffset.UtcNow, null, null));
        }
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) {
            Enqueued.AddRange(requests);
            return Task.FromResult(requests.Count);
        }
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) => Task.FromResult<JobRunSnapshot?>(null);
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) {
            ProgressMessages.Add(message);
            return Task.CompletedTask;
        }
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobQueueCount>>([]);
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
