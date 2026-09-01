using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;

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
        Assert.Equal(JobType.GenerateTrickplay, request.Type);
        Assert.Equal(videoId.ToString(), request.TargetEntityId);
    }

    [Fact]
    public async Task VideoScanInvalidatesAndQueuesSubtitlesForEverySharedFileOwner() {
        var root = new LibraryRootData(
            Guid.NewGuid(), "/media/videos", "Videos",
            Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        const string sourcePath = "/media/videos/show-s01e01-e02.mkv";
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();
        var needsSubtitles = new DownstreamNeeds(
            NeedsProbe: false,
            MissingOshash: false,
            MissingMd5: false,
            NeedsPreview: false,
            NeedsTrickplay: false,
            NeedsSubtitleExtraction: true,
            NeedsGridThumbnail: false);
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
            UpsertedVideoIds = [firstOwner],
            PlayableVideoSourceOwners = [
                new PlayableVideoSourceOwner(firstOwner, sourcePath, EntityKind.Video),
                new PlayableVideoSourceOwner(secondOwner, sourcePath, EntityKind.Video)
            ],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [firstOwner] = needsSubtitles,
                [secondOwner] = needsSubtitles
            }
        };
        var sidecarSignature = new string('a', 64);
        var queue = new RecordingJobQueue();
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            new RecordingFileDiscovery([sourcePath]),
            persistence,
            persistence,
            persistence,
            subtitleSidecars: new FixedSubtitleSidecarDiscovery(
                new VideoSubtitleSidecarDiscovery(sourcePath, [], sidecarSignature, IsComplete: true)));

        await handler.HandleAsync(
            new JobContext(SingleRootScanJob(root), queue), CancellationToken.None);

        var subtitleJobs = queue.Enqueued.Where(request => request.Type == JobType.ExtractSubtitles).ToArray();
        Assert.Equal(2, subtitleJobs.Length);
        Assert.Equal(
            new[] { firstOwner, secondOwner }.Order().ToArray(),
            subtitleJobs.Select(request => Guid.Parse(request.TargetEntityId!)).Order().ToArray());
        Assert.Equal(
            new[] { firstOwner, secondOwner }.Order().ToArray(),
            persistence.InvalidatedSubtitleStates.Select(state => state.EntityId).Order().ToArray());
        Assert.All(persistence.InvalidatedSubtitleStates, state => Assert.Equal(sidecarSignature, state.Signature));
    }

    [Fact]
    public async Task UnchangedVideoScanQueuesNoDownstreamWork() {
        var root = new LibraryRootData(
            Guid.NewGuid(), "/media/videos", "Videos",
            Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        const string sourcePath = "/media/videos/movie.mkv";
        var videoId = Guid.NewGuid();
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
            ExistingVideoTargets = [new PlayableVideoRefreshSourceTarget(videoId, "Movie", sourcePath, EntityKind.Movie)],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: true,
                    NeedsGridThumbnail: false)
            }
        };
        var snapshots = new FakeScanSnapshotStore();
        snapshots.Seed(root.Id, JobType.ScanLibrary.ToCode(), [sourcePath]);
        var queue = new RecordingJobQueue();
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            new CategoryFileDiscovery([sourcePath], []),
            persistence,
            persistence,
            persistence,
            snapshots);

        await handler.HandleAsync(
            new JobContext(SingleRootScanJob(root), queue), CancellationToken.None);

        Assert.Empty(queue.Enqueued);
        Assert.Equal(0, persistence.PlayableVideoRecoveryTargetCalls);
        Assert.Equal(0, persistence.DownstreamNeedsChecks);
    }

    [Fact]
    public async Task VideoScanRemovesOrphanTagsWhenSettingEnabled() {
        var persistence = new FakeScanPersistence([OrphanCleanupRoot]) {
            Settings = OrphanCleanupSettings(removeOrphanTags: true),
        };
        var handler = OrphanCleanupHandler(persistence);

        await handler.HandleAsync(
            new JobContext(OrphanCleanupJob, new RecordingJobQueue()), CancellationToken.None);

        // Library-wide cleanup is reserved for deep integrity scans; it fires even though this
        // root has no files and the detailed pass does nothing.
        Assert.Equal(1, persistence.RemoveOrphanTagsCalls);
    }

    [Fact]
    public async Task RoutineVideoScanSkipsLibraryWideCleanup() {
        // Ordinary root scans stay scoped to their delta; the outside-root and orphan-tag
        // sweeps run only on deep integrity scans.
        var persistence = new FakeScanPersistence([OrphanCleanupRoot]) {
            Settings = OrphanCleanupSettings(removeOrphanTags: true),
        };
        var handler = OrphanCleanupHandler(persistence);
        var routineJob = OrphanCleanupJob with {
            PayloadJson = $$"""{"libraryRootId":"{{OrphanCleanupRoot.Id}}"}"""
        };

        await handler.HandleAsync(
            new JobContext(routineJob, new RecordingJobQueue()), CancellationToken.None);

        Assert.Equal(0, persistence.RemoveOrphanTagsCalls);
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
        PayloadJson: $$"""{"libraryRootId":"{{OrphanCleanupRoot.Id}}","deep":true}""",
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

    [Theory]
    [InlineData(false, JobType.GenerateAudioWaveform)]
    [InlineData(true, JobType.ProbeAudio)]
    public async Task AudioScanPlansReadinessBeforeWaveform(bool needsProbe, JobType expectedJob) {
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
            DefaultDownstreamNeeds = new(
                NeedsProbe: needsProbe,
                MissingOshash: false,
                MissingMd5: false,
                NeedsPreview: true,
                NeedsTrickplay: false,
                NeedsSubtitleExtraction: false,
                NeedsGridThumbnail: false)
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
        Assert.Equal(expectedJob, request.Type);
        Assert.Equal(EntityKind.AudioTrack.ToCode(), request.TargetEntityKind);
        Assert.Equal(track.Id.ToString(), request.TargetEntityId);
    }

    [Fact]
    public async Task UnchangedAudioScanQueuesNoWaveformRecoveryWork() {
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
            ExistingAudioTrackTargets = [new EntityRefreshTarget(trackId, EntityKind.AudioTrack.ToCode(), "song")],
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

        Assert.Empty(queue.Enqueued);
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
    public async Task VideoScanKeepsTitledSiblingFoldersInsideEstablishedSeries() {
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
        var videoIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings,
            UpsertedVideoIds = videoIds
        };
        // Discovery order deliberately differs from folder-title order. Season 1 establishes the
        // parent as the series; the other direct child folders follow it alphabetically as seasons.
        var discovery = new RecordingFileDiscovery([
            "/media/videos/Series Root/Random Title Folder/Zeta.mp4",
            "/media/videos/Series Root/Season 1/Episode One.mp4",
            "/media/videos/Series Root/Other Title/Only Feature.mp4",
            "/media/videos/Series Root/Random Title Folder/Alpha.mp4"
        ]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence);

        await handler.HandleAsync(
            new JobContext(SingleRootScanJob(root), new RecordingJobQueue()),
            CancellationToken.None);

        Assert.Equal(4, persistence.UpsertedVideoItems.Count);
        Assert.All(persistence.UpsertedVideoItems, item => {
            Assert.Equal("Series Root", item.Series?.Title);
            Assert.Equal("/media/videos/Series Root", item.Series?.FolderPath);
            Assert.NotNull(item.Season);
            Assert.Null(item.Movie);
        });

        var numberedSeason = Assert.Single(
            persistence.UpsertedVideoItems,
            item => item.Season?.Title == "Season 1");
        Assert.Equal(1, numberedSeason.Season?.SeasonNumber);

        var otherTitleSeason = Assert.Single(
            persistence.UpsertedVideoItems,
            item => item.Season?.Title == "Other Title");
        Assert.Equal(2, otherTitleSeason.Season?.SeasonNumber);

        var randomTitleSeason = persistence.UpsertedVideoItems
            .Where(item => item.Season?.Title == "Random Title Folder")
            .ToArray();
        Assert.Equal(2, randomTitleSeason.Length);
        Assert.All(randomTitleSeason, item => Assert.Equal(3, item.Season?.SeasonNumber));
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
        // Standard media-manager layout: clean folder name, release-style filename that does not begin
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
    public async Task GalleryScanUsesImageProcessingPolicyForFingerprintAndThumbnail() {
        var root = new LibraryRootData(
            Guid.NewGuid(), "/media/images", "Images",
            Enabled: true, Recursive: true,
            ScanVideos: false, ScanImages: true, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        var persistence = new FakeScanPersistence([root]) {
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: false,
                AutoGenerateOshash: true,
                AutoGenerateMd5: false,
                AutoGeneratePreview: true,
                GenerateTrickplay: false,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2),
            DefaultDownstreamNeeds = new(
                NeedsProbe: false,
                MissingOshash: true,
                MissingMd5: false,
                NeedsPreview: true,
                NeedsTrickplay: false,
                NeedsSubtitleExtraction: false,
                NeedsGridThumbnail: false)
        };
        var queue = new RecordingJobQueue();
        var handler = new ScanGalleryJobHandler(
            NullLogger<ScanGalleryJobHandler>.Instance,
            new RecordingFileDiscovery(directoryGroups: new Dictionary<string, IReadOnlyList<string>> {
                [root.Path] = ["/media/images/cover.png"]
            }),
            persistence,
            persistence,
            persistence);

        await handler.HandleAsync(new JobContext(GalleryJob(root), queue), CancellationToken.None);

        Assert.Collection(queue.Enqueued,
            request => Assert.Equal(JobType.FingerprintImage, request.Type),
            request => Assert.Equal(JobType.GenerateImageThumbnail, request.Type));
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
    public async Task ComicScanMaterializesSeriesVolumesInstallmentsAndPageManifests() {
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
                    AutoGeneratePreview: true,
                    GenerateTrickplay: false,
                    TrickplayIntervalSeconds: 10,
                    PreviewClipDurationSeconds: 8,
                    ThumbnailQuality: 2,
                    TrickplayQuality: 2),
                DefaultDownstreamNeeds = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: true,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: false,
                    NeedsGridThumbnail: false)
            };
            var manifests = new RecordingPageManifestStore();
            var handler = new ScanComicJobHandler(
                NullLogger<ScanComicJobHandler>.Instance,
                new RecordingFileDiscovery([chapterTwoPath, chapterOnePath]),
                persistence,
                persistence,
                manifests,
                persistence);
            var job = new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.ScanComic,
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

            var queue = new RecordingJobQueue();
            await handler.HandleAsync(new JobContext(job, queue), CancellationToken.None);

            var series = Assert.Single(persistence.UpsertedComicSeries);
            Assert.Equal(Path.Combine(rootPath, "Promised Neverland"), series.FolderPath);
            Assert.Equal("Promised Neverland", series.Title);

            var volume = Assert.Single(persistence.UpsertedComicVolumes);
            Assert.Equal("Volume 01", volume.Title);
            Assert.Equal(series.Id, volume.SeriesEntityId);
            Assert.Equal(1, volume.VolumeNumber);

            Assert.Collection(
                persistence.UpsertedComicInstallments,
                installment => {
                    Assert.Equal(chapterOnePath, installment.ArchivePath);
                    Assert.Equal("Promised Neverland Ch.1", installment.Title);
                    Assert.Equal(volume.Id, installment.ParentEntityId);
                    Assert.Equal(0, installment.SortOrder);
                    Assert.Equal(1, installment.Position);
                },
                installment => {
                    Assert.Equal(chapterTwoPath, installment.ArchivePath);
                    Assert.Equal("Promised Neverland Ch.2", installment.Title);
                    Assert.Equal(volume.Id, installment.ParentEntityId);
                    Assert.Equal(1, installment.SortOrder);
                    Assert.Equal(2, installment.Position);
                });

            Assert.Collection(
                manifests.Manifests.OrderBy(manifest => manifest.Pages.Count),
                manifest => Assert.Equal(["001.jpg"], manifest.Pages.Select(page => page.ArchiveMember)),
                manifest => Assert.Equal(["001.jpg", "002.jpg"], manifest.Pages.Select(page => page.ArchiveMember)));
            Assert.All(manifests.Manifests, manifest => {
                Assert.Equal(PageReadingDirection.LeftToRight, manifest.Direction);
                Assert.Equal(ReaderMode.Paged, manifest.DefaultMode);
                Assert.Equal(0, manifest.CoverOrdinal);
            });
            Assert.Empty(persistence.UpsertedBooks);
            Assert.Equal([chapterOnePath, chapterTwoPath], persistence.ValidComicArchivePaths);
            Assert.Equal([root.Id], persistence.LastScannedRootIds);
            var thumbnailJob = Assert.Single(queue.Enqueued);
            Assert.Equal(JobType.GenerateGridThumbnail, thumbnailJob.Type);
            Assert.Equal(EntityKind.ComicSeries.ToCode(), thumbnailJob.TargetEntityKind);
            Assert.Equal(series.Id.ToString(), thumbnailJob.TargetEntityId);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ComicScanUsesManagedArchiveAsSourceAndRetainedFolderAsProvenance() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-loose-comic-scan-");
        try {
            var rootPath = tempRoot.FullName;
            var originPath = Path.Combine(rootPath, "Promised Neverland", "Chapter 2");
            Directory.CreateDirectory(originPath);
            var generatedPath = Path.Combine(rootPath, "managed-chapter.cbz");
            CreateZip(generatedPath, ["001.jpg"]);
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
            var normalizer = new RecordingComicFolderNormalizer(new NormalizedComicArchive(
                generatedPath,
                originPath + ".cbz",
                originPath,
                "source-signature"));
            var handler = new ScanComicJobHandler(
                NullLogger<ScanComicJobHandler>.Instance,
                new RecordingFileDiscovery([]),
                persistence,
                persistence,
                new RecordingPageManifestStore(),
                persistence,
                folderNormalizer: normalizer);
            var job = new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.ScanComic,
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

            await handler.HandleAsync(
                new JobContext(job, new RecordingJobQueue()),
                CancellationToken.None);

            var series = Assert.Single(persistence.UpsertedComicSeries);
            Assert.Equal(Path.Combine(rootPath, "Promised Neverland"), series.FolderPath);
            var installment = Assert.Single(persistence.UpsertedComicInstallments);
            Assert.Equal(generatedPath, installment.ArchivePath);
            Assert.Equal("Chapter 2", installment.Title);
            Assert.Equal(new ComicSourceProvenance(originPath, "source-signature"), installment.SourceProvenance);
            Assert.Equal([generatedPath], normalizer.LastRetainedPaths);
            Assert.Equal([generatedPath], persistence.ValidComicArchivePaths);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ComicScanSkipsAlreadyOrganizedAutoIdentifyRootsWhenUnorganizedOnly() {
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
                    AutoIdentifyKinds: ["comic-series"],
                    AutoIdentifyUnorganizedOnly: true),
                OrganizedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { bookPath }
            };
            var queue = new RecordingJobQueue();
            var handler = new ScanComicJobHandler(
                NullLogger<ScanComicJobHandler>.Instance,
                new RecordingFileDiscovery([archivePath]),
                persistence,
                persistence,
                new RecordingPageManifestStore(),
                persistence);
            var job = new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.ScanComic,
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
    public async Task ComicScanReadsComicInfoForHierarchyMetadataAndDirection() {
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
                Number = "12",
                Volume = 3,
                Manga = "YesAndRightToLeft",
                Pages = [new ComicInfoPageMetadata(
                    0,
                    PageType.BackCover,
                    IsDoublePage: true,
                    Width: 2200,
                    Height: 1600)],
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
            var manifests = new RecordingPageManifestStore();
            var handler = new ScanComicJobHandler(
                NullLogger<ScanComicJobHandler>.Instance,
                new RecordingFileDiscovery([archivePath]),
                persistence,
                persistence,
                manifests,
                persistence,
                comicInfoReader: new StubComicInfoMetadataReader(metadata),
                scanMetadata: metadataPersistence);
            var job = new JobRunSnapshot(
                Guid.NewGuid(),
                JobType.ScanComic,
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

            var series = Assert.Single(persistence.UpsertedComicSeries);
            Assert.Equal("Metadata Series", series.Title);
            var volume = Assert.Single(persistence.UpsertedComicVolumes);
            Assert.Equal(3, volume.VolumeNumber);
            var installment = Assert.Single(persistence.UpsertedComicInstallments);
            Assert.Equal("Metadata Chapter", installment.Title);
            Assert.Equal(volume.Id, installment.ParentEntityId);
            Assert.Equal(12, installment.Position);
            Assert.Equal("12", installment.PositionLabel);
            Assert.Equal(ComicInstallmentKind.Chapter, installment.InstallmentKind);
            Assert.Collection(
                metadataPersistence.AppliedComics,
                applied => {
                    Assert.Equal(series.Id, applied.EntityId);
                    Assert.Null(applied.Metadata.Summary);
                    Assert.True(applied.MarkNsfw);
                },
                applied => {
                    Assert.Equal(installment.Id, applied.EntityId);
                    Assert.Same(metadata, applied.Metadata);
                    Assert.True(applied.MarkNsfw);
                });
            var manifest = Assert.Single(manifests.Manifests);
            Assert.Equal(PageReadingDirection.RightToLeft, manifest.Direction);
            var page = Assert.Single(manifest.Pages);
            Assert.Equal(PageType.BackCover, page.PageType);
            Assert.True(page.IsDoublePage);
            Assert.Equal(2200, page.Width);
            Assert.Equal(1600, page.Height);
            Assert.Equal(0, manifest.CoverOrdinal);
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
            Assert.Empty(persistence.UpsertedAudiobookBooks);

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
    public async Task BookScanAttachesOrderedAudiobookTracksBesideReadableBook() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-audiobook-mixed-");
        try {
            var rootPath = tempRoot.FullName;
            var bookPath = Path.Combine(rootPath, "Frank Herbert", "Dune");
            Directory.CreateDirectory(bookPath);
            var epubPath = Path.Combine(bookPath, "Dune.epub");
            var secondTrack = Path.Combine(bookPath, "02 - Muad'Dib.mp3");
            var firstTrack = Path.Combine(bookPath, "01 - Arrakis.m4b");
            await File.WriteAllTextAsync(epubPath, "epub");
            await File.WriteAllTextAsync(firstTrack, "audio");
            await File.WriteAllTextAsync(secondTrack, "audio");
            var root = new LibraryRootData(
                Guid.NewGuid(), rootPath, "Books", true, true,
                ScanVideos: false, ScanImages: false, ScanAudio: false, ScanBooks: true, IsNsfw: false);
            var persistence = new FakeScanPersistence([root]) { Settings = DisabledGeneratedWorkSettings };
            var handler = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([secondTrack, epubPath, firstTrack]),
                persistence,
                persistence,
                persistence,
                audio: persistence);

            await handler.HandleAsync(
                new JobContext(SingleRootScanJob(root), new RecordingJobQueue()),
                CancellationToken.None);

            var book = Assert.Single(persistence.UpsertedBooks);
            Assert.Equal(epubPath, book.SourcePath);
            Assert.Collection(
                persistence.UpsertedAudioTracks.OrderBy(track => track.SortOrder),
                track => {
                    Assert.Equal(firstTrack, track.FilePath);
                    Assert.Equal(book.Id, track.AudioLibraryEntityId);
                },
                track => {
                    Assert.Equal(secondTrack, track.FilePath);
                    Assert.Equal(book.Id, track.AudioLibraryEntityId);
                });
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BookScanCreatesAudioOnlyBookForStandaloneM4b() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-audiobook-only-");
        try {
            var rootPath = tempRoot.FullName;
            var sourcePath = Path.Combine(rootPath, "Project Hail Mary.m4b");
            await File.WriteAllTextAsync(sourcePath, "audio");
            var root = new LibraryRootData(
                Guid.NewGuid(), rootPath, "Books", true, true,
                ScanVideos: false, ScanImages: false, ScanAudio: false, ScanBooks: true, IsNsfw: false);
            var persistence = new FakeScanPersistence([root]) { Settings = DisabledGeneratedWorkSettings };
            var handler = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([sourcePath]),
                persistence,
                persistence,
                persistence,
                audio: persistence);

            await handler.HandleAsync(
                new JobContext(SingleRootScanJob(root), new RecordingJobQueue()),
                CancellationToken.None);

            var book = Assert.Single(persistence.UpsertedAudiobookBooks);
            Assert.Equal(sourcePath, book.SourcePath);
            Assert.Equal(BookFormat.Audio, book.Format);
            var track = Assert.Single(persistence.UpsertedAudioTracks);
            Assert.Equal(book.Id, track.AudioLibraryEntityId);
            Assert.Equal(sourcePath, track.FilePath);
            Assert.Equal([sourcePath], persistence.ValidAudioTrackPathsByLibraryId[book.Id]);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BookScanMatchesRootAudiobooksToReadableBookByBasename() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-audiobook-pairs-");
        try {
            var rootPath = tempRoot.FullName;
            var duneEpub = Path.Combine(rootPath, "Dune.epub");
            var duneAudio = Path.Combine(rootPath, "Dune.m4b");
            var messiahEpub = Path.Combine(rootPath, "Dune Messiah.epub");
            var messiahAudio = Path.Combine(rootPath, "Dune Messiah.mp3");
            foreach (var path in new[] { duneEpub, duneAudio, messiahEpub, messiahAudio }) {
                await File.WriteAllTextAsync(path, "media");
            }
            var root = new LibraryRootData(
                Guid.NewGuid(), rootPath, "Books", true, true,
                ScanVideos: false, ScanImages: false, ScanAudio: false, ScanBooks: true, IsNsfw: false);
            var persistence = new FakeScanPersistence([root]) { Settings = DisabledGeneratedWorkSettings };
            var handler = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([messiahAudio, duneEpub, duneAudio, messiahEpub]),
                persistence, persistence, persistence, audio: persistence);

            await handler.HandleAsync(
                new JobContext(SingleRootScanJob(root), new RecordingJobQueue()),
                CancellationToken.None);

            var dune = persistence.UpsertedBooks.Single(book => book.SourcePath == duneEpub);
            var messiah = persistence.UpsertedBooks.Single(book => book.SourcePath == messiahEpub);
            Assert.Equal(dune.Id, persistence.UpsertedAudioTracks.Single(track => track.FilePath == duneAudio).AudioLibraryEntityId);
            Assert.Equal(messiah.Id, persistence.UpsertedAudioTracks.Single(track => track.FilePath == messiahAudio).AudioLibraryEntityId);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BookScanRemovesLastAudiobookTrackWhileKeepingReadableBook() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-audiobook-stale-");
        try {
            var rootPath = tempRoot.FullName;
            var epubPath = Path.Combine(rootPath, "Dune.epub");
            await File.WriteAllTextAsync(epubPath, "epub");
            var root = new LibraryRootData(
                Guid.NewGuid(), rootPath, "Books", true, true,
                ScanVideos: false, ScanImages: false, ScanAudio: false, ScanBooks: true, IsNsfw: false);
            var persistence = new FakeScanPersistence([root]) { Settings = DisabledGeneratedWorkSettings };
            var handler = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([epubPath]),
                persistence, persistence, persistence, audio: persistence);

            await handler.HandleAsync(
                new JobContext(SingleRootScanJob(root), new RecordingJobQueue()),
                CancellationToken.None);

            var book = Assert.Single(persistence.UpsertedBooks);
            Assert.True(persistence.ValidAudioTrackPathsByLibraryId.TryGetValue(book.Id, out var validPaths));
            Assert.Empty(validPaths!);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ImportedAudiobookAttachesToExistingReadableBookWithoutReplacingItsSource() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-audiobook-import-");
        try {
            var rootPath = tempRoot.FullName;
            var audioPath = Path.Combine(rootPath, "Dune.m4b");
            await File.WriteAllTextAsync(audioPath, "audio");
            var root = new LibraryRootData(
                Guid.NewGuid(), rootPath, "Books", true, true,
                ScanVideos: false, ScanImages: false, ScanAudio: false, ScanBooks: true, IsNsfw: false);
            var existingBookId = Guid.NewGuid();
            var persistence = new FakeScanPersistence([root]) { Settings = DisabledGeneratedWorkSettings };
            var handler = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([audioPath]),
                persistence,
                persistence,
                persistence,
                acquisitionHints: new FixedAcquisitionHintApplier(existingBookId),
                audio: persistence);

            await handler.MaterializeImportedPathsAsync(
                new JobContext(SingleRootScanJob(root), new RecordingJobQueue()),
                Guid.NewGuid(),
                root,
                [audioPath],
                CancellationToken.None);

            Assert.Empty(persistence.UpsertedAudiobookBooks);
            var track = Assert.Single(persistence.UpsertedAudioTracks);
            Assert.Equal(existingBookId, track.AudioLibraryEntityId);
        } finally {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ImportedMultipartAudiobookKeepsRequestedBookAfterFullReconciliation() {
        var tempRoot = Directory.CreateTempSubdirectory("prismedia-audiobook-import-reconcile-");
        try {
            var rootPath = tempRoot.FullName;
            var readableFolder = Directory.CreateDirectory(Path.Combine(rootPath, "Legacy Library")).FullName;
            var epubPath = Path.Combine(readableFolder, "A Game of Thrones.epub");
            await File.WriteAllTextAsync(epubPath, "epub");
            var audioFolder = Directory.CreateDirectory(
                Path.Combine(rootPath, "George R. R. Martin", "A Game of Thrones (1996)")).FullName;
            var audioPaths = Enumerable.Range(1, 76)
                .Select(index => Path.Combine(audioFolder, $"Chapter {index:00}.mp3"))
                .ToArray();
            foreach (var path in audioPaths) {
                await File.WriteAllTextAsync(path, "audio");
            }

            var root = new LibraryRootData(
                Guid.NewGuid(), rootPath, "Books", true, true,
                ScanVideos: false, ScanImages: false, ScanAudio: false, ScanBooks: true, IsNsfw: false);
            var persistence = new FakeScanPersistence([root]) { Settings = DisabledGeneratedWorkSettings };

            var initialScan = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([epubPath]),
                persistence, persistence, persistence, audio: persistence);
            await initialScan.HandleAsync(
                new JobContext(SingleRootScanJob(root), new RecordingJobQueue()),
                CancellationToken.None);
            var requestedBookId = Assert.Single(persistence.UpsertedBooks).Id;

            var acquisitionHints = new FixedAcquisitionHintApplier(requestedBookId);
            var importMaterializer = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery(audioPaths),
                persistence,
                persistence,
                persistence,
                acquisitionHints: acquisitionHints,
                audio: persistence);
            await importMaterializer.MaterializeImportedPathsAsync(
                new JobContext(SingleRootScanJob(root), new RecordingJobQueue()),
                Guid.NewGuid(),
                root,
                audioPaths,
                CancellationToken.None);
            Assert.All(
                persistence.UpsertedAudioTracks.TakeLast(audioPaths.Length),
                track => Assert.Equal(requestedBookId, track.AudioLibraryEntityId));

            var fullScan = new ScanBookJobHandler(
                NullLogger<ScanBookJobHandler>.Instance,
                new RecordingFileDiscovery([epubPath, .. audioPaths]),
                persistence,
                persistence,
                persistence,
                acquisitionHints: acquisitionHints,
                audio: persistence);
            await fullScan.HandleAsync(
                new JobContext(SingleRootScanJob(root), new RecordingJobQueue()),
                CancellationToken.None);

            Assert.All(
                persistence.UpsertedAudioTracks.TakeLast(audioPaths.Length),
                track => Assert.Equal(requestedBookId, track.AudioLibraryEntityId));
            Assert.DoesNotContain(
                persistence.UpsertedAudiobookBooks,
                book => string.Equals(book.SourcePath, audioFolder, StringComparison.OrdinalIgnoreCase));
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
                bookFileMetadata: new StubBookFileMetadataReader(new BookFileMetadata {
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

    private sealed class StubBookFileMetadataReader(BookFileMetadata metadata) : IBookFileMetadataReader {
        public Task<BookFileMetadata?> ReadAsync(string sourcePath, BookFormat format, CancellationToken cancellationToken) =>
            Task.FromResult<BookFileMetadata?>(metadata);
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
    public async Task VideoSnapshotRescansWhenOnlyAnAdjacentSubtitleIsAdded() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos", "Videos",
            Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        var videoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var videoPath = "/media/videos/movie.mkv";
        var persistence = new FakeScanPersistence([root]) { UpsertedVideoIds = [videoId] };
        var snapshots = new FakeScanSnapshotStore();
        var job = SingleRootScanJob(root);

        var first = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            new CategoryFileDiscovery([videoPath], []),
            persistence,
            persistence,
            persistence,
            snapshots);
        await first.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None);
        Assert.Single(persistence.UpsertedVideoItems);

        var second = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            new CategoryFileDiscovery([videoPath], ["/media/videos/movie.en.srt"]),
            persistence,
            persistence,
            persistence,
            snapshots);
        await second.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None);

        Assert.Equal(2, persistence.UpsertedVideoItems.Count);
        Assert.Equal(2, snapshots.ApplyCount);
    }

    [Fact]
    public async Task ChangedVideoSignatureInvalidatesEveryByteDerivedAssetBeforeReconcile() {
        var root = new LibraryRootData(
            Guid.NewGuid(), "/media/videos", "Videos",
            Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        const string videoPath = "/media/videos/movie.mkv";
        const string untouchedPath = "/media/videos/other.mkv";
        var videoId = Guid.NewGuid();
        var persistence = new FakeScanPersistence([root]) {
            UpsertedVideoIds = [videoId]
        };
        var snapshots = new FakeScanSnapshotStore();
        snapshots.Seed(root.Id, JobType.ScanLibrary.ToCode(), [videoPath, untouchedPath]);
        var discovery = new ChangedSignatureFileDiscovery(videoPath, [untouchedPath]);
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            discovery,
            persistence,
            persistence,
            persistence,
            snapshots);

        await handler.HandleAsync(
            new JobContext(SingleRootScanJob(root), new RecordingJobQueue()),
            CancellationToken.None);

        Assert.Equal([(videoPath, videoPath)], persistence.ReboundVideoPaths);
        Assert.Equal(videoPath, Assert.Single(persistence.UpsertedVideoItems).FilePath);
        Assert.Equal(0, discovery.DetailedDiscoveryCalls);
    }

    [Fact]
    public async Task DurableFileChangeScansOnlyTheChangedFilesParentDirectory() {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"prismedia-scan-scope-{Guid.NewGuid():N}");
        var season = Path.Combine(tempRoot, "Series", "Season 01");
        Directory.CreateDirectory(season);
        var changedPath = Path.Combine(season, "S01E01.mkv");
        var untouchedPath = Path.Combine(season, "S01E02.mkv");
        await File.WriteAllBytesAsync(changedPath, [1]);
        await File.WriteAllBytesAsync(untouchedPath, [2]);
        try {
            var root = new LibraryRootData(
                Guid.NewGuid(), tempRoot, "TV",
                Enabled: true, Recursive: true,
                ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
            var videoId = Guid.NewGuid();
            var persistence = new FakeScanPersistence([root]) { UpsertedVideoIds = [videoId] };
            var snapshots = new FakeScanSnapshotStore();
            snapshots.Seed(root.Id, JobType.ScanLibrary.ToCode(), [changedPath, untouchedPath]);
            var intake = new FixedChangeIntake(changedPath);
            var discovery = new ScopedChangeFileDiscovery(changedPath, untouchedPath);
            var handler = new ScanLibraryJobHandler(
                NullLogger<ScanLibraryJobHandler>.Instance,
                discovery,
                persistence,
                persistence,
                persistence,
                snapshots,
                changeIntake: intake);
            var job = SingleRootScanJob(root) with {
                PayloadJson = new ScanRootPayload(root.Id, ChangesOnly: true).ToJson()
            };

            await handler.HandleAsync(new JobContext(job, new RecordingJobQueue()), CancellationToken.None);

            Assert.Equal(changedPath, Assert.Single(persistence.UpsertedVideoItems).FilePath);
            Assert.Equal(0, discovery.DetailedDiscoveryCalls);
            Assert.NotEmpty(discovery.SignatureRoots);
            Assert.All(discovery.SignatureRoots, path => Assert.Equal(season, path));
            Assert.Equal(1, intake.CompletionCount);
        } finally {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task IncompleteSidecarDiscoveryDoesNotAdvanceTheVideoSnapshot() {
        var root = new LibraryRootData(
            Guid.NewGuid(), "/media/videos", "Videos",
            Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        const string videoPath = "/media/videos/movie.mkv";
        const string sidecarPath = "/media/videos/movie.en.srt";
        var videoId = Guid.NewGuid();
        var persistence = new FakeScanPersistence([root]) {
            UpsertedVideoIds = [videoId],
            PlayableVideoSourceOwners = [new PlayableVideoSourceOwner(videoId, videoPath, EntityKind.Video)]
        };
        var snapshots = new FakeScanSnapshotStore();
        snapshots.Seed(root.Id, JobType.ScanLibrary.ToCode(), [videoPath]);
        var files = new CategoryFileDiscovery([videoPath], [sidecarPath]);
        var incomplete = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            files,
            persistence,
            persistence,
            persistence,
            snapshots,
            subtitleSidecars: new FixedSubtitleSidecarDiscovery(
                new VideoSubtitleSidecarDiscovery(videoPath, [], new string('a', 64), IsComplete: false)));

        await Assert.ThrowsAsync<IOException>(() => incomplete.HandleAsync(
            new JobContext(SingleRootScanJob(root), new NoopJobQueue()), CancellationToken.None));
        Assert.Equal(0, snapshots.ApplyCount);

        var complete = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            files,
            persistence,
            persistence,
            persistence,
            snapshots,
            subtitleSidecars: new FixedSubtitleSidecarDiscovery(
                new VideoSubtitleSidecarDiscovery(videoPath, [], new string('b', 64), IsComplete: true)));
        await complete.HandleAsync(
            new JobContext(SingleRootScanJob(root), new NoopJobQueue()), CancellationToken.None);

        Assert.Equal(1, snapshots.ApplyCount);
    }

    [Fact]
    public async Task ExistingOwnerStillQueuesSidecarWorkWhenItsUpsertFails() {
        var root = new LibraryRootData(
            Guid.NewGuid(), "/media/videos", "Videos",
            Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        const string videoPath = "/media/videos/movie.mkv";
        const string sidecarPath = "/media/videos/movie.en.srt";
        var videoId = Guid.NewGuid();
        var persistence = new FakeScanPersistence([root]) {
            Settings = DisabledGeneratedWorkSettings,
            VideoUpsertException = new IOException("existing row could not be updated"),
            PlayableVideoSourceOwners = [new PlayableVideoSourceOwner(videoId, videoPath, EntityKind.Video)],
            DownstreamNeedsById = new Dictionary<Guid, DownstreamNeeds> {
                [videoId] = new(
                    NeedsProbe: false,
                    MissingOshash: false,
                    MissingMd5: false,
                    NeedsPreview: false,
                    NeedsTrickplay: false,
                    NeedsSubtitleExtraction: true,
                    NeedsGridThumbnail: false)
            }
        };
        var snapshots = new FakeScanSnapshotStore();
        snapshots.Seed(root.Id, JobType.ScanLibrary.ToCode(), [videoPath]);
        var queue = new RecordingJobQueue();
        var handler = new ScanLibraryJobHandler(
            NullLogger<ScanLibraryJobHandler>.Instance,
            new CategoryFileDiscovery([videoPath], [sidecarPath]),
            persistence,
            persistence,
            persistence,
            snapshots,
            subtitleSidecars: new FixedSubtitleSidecarDiscovery(
                new VideoSubtitleSidecarDiscovery(
                    videoPath,
                    [],
                    new string('d', 64),
                    IsComplete: true)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new JobContext(SingleRootScanJob(root), queue),
                CancellationToken.None));

        Assert.Equal(videoId, Assert.Single(persistence.InvalidatedSubtitleStates).EntityId);
        var request = Assert.Single(queue.Enqueued, item => item.Type == JobType.ExtractSubtitles);
        Assert.Equal(videoId.ToString(), request.TargetEntityId);
        Assert.Equal(1, snapshots.ApplyCount);
    }

    [Fact]
    public async Task FailedFilesAreWithheldFromSnapshotAndRetriedNextScan() {
        var root = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/videos", "Videos",
            Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        var persistence = new FakeScanPersistence([root]);
        var snapshots = new FakeScanSnapshotStore();
        var job = SingleRootScanJob(root);

        // First scan persists a.mkv but fails on b.mkv: the job fails, yet the snapshot advances
        // for a.mkv and withholds b.mkv so exactly it is retried.
        var first = new RecordingScanHandler(
            persistence, snapshots, new RecordingFileDiscovery(["/media/videos/a.mkv", "/media/videos/b.mkv"])) {
            Outcome = new ScanRootOutcome(["/media/videos/b.mkv"])
        };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            first.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None));
        Assert.Contains("b.mkv", ex.Message);

        var stored = await snapshots.LoadAsync(root.Id, JobType.ScanLibrary.ToCode(), CancellationToken.None);
        Assert.Contains(stored, signature => signature.Path == "/media/videos/a.mkv");
        Assert.DoesNotContain(stored, signature => signature.Path == "/media/videos/b.mkv");

        // Second scan with identical files on disk: b.mkv still reads as added, so the detailed
        // scan runs again instead of being skipped by the incremental fast path.
        var second = new RecordingScanHandler(
            persistence, snapshots, new RecordingFileDiscovery(["/media/videos/a.mkv", "/media/videos/b.mkv"]));
        await second.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None);
        Assert.Equal([root.Id], second.ScannedRootIds);

        var healed = await snapshots.LoadAsync(root.Id, JobType.ScanLibrary.ToCode(), CancellationToken.None);
        Assert.Contains(healed, signature => signature.Path == "/media/videos/b.mkv");
    }

    [Fact]
    public async Task AllRootsScanKeepsScanningWhenOneRootFails() {
        var brokenRoot = new LibraryRootData(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/media/broken", "Broken",
            Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        var healthyRoot = new LibraryRootData(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "/media/healthy", "Healthy",
            Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false, IsNsfw: false);
        var persistence = new FakeScanPersistence([brokenRoot, healthyRoot]);
        var handler = new RecordingScanHandler(persistence) {
            OutcomeSelector = root => root.Id == brokenRoot.Id
                ? new ScanRootOutcome(["/media/broken/bad.mkv"])
                : ScanRootOutcome.Success
        };
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

        // The broken root's failure is reported, but the healthy root still scans.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None));

        Assert.Contains("1 of 2", ex.Message);
        Assert.Equal([brokenRoot.Id, healthyRoot.Id], handler.ScannedRootIds);
        Assert.Contains(healthyRoot.Id, persistence.LastScannedRootIds);
        Assert.DoesNotContain(brokenRoot.Id, persistence.LastScannedRootIds);
    }

    [Fact]
    public async Task SnapshotNoChangeScanDoesNotQueuePendingAutoIdentifyRoots() {
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

        Assert.Empty(secondQueue.Enqueued);
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

        protected override bool IsEligibleRoot(LibraryRootData root) => root.ScanVideos;

        protected override IReadOnlyList<MediaCategory> ScanCategories => [MediaCategory.Video];

        public ScanRootOutcome Outcome { get; set; } = ScanRootOutcome.Success;
        public Func<LibraryRootData, ScanRootOutcome>? OutcomeSelector { get; set; }

        protected override Task<ScanRootOutcome> ScanRootCoreAsync(
            JobContext context,
            LibraryRootData root,
            CancellationToken cancellationToken) {
            ScannedRootIds.Add(root.Id);
            return Task.FromResult(OutcomeSelector?.Invoke(root) ?? Outcome);
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

    private sealed class FakeScanPersistence(IReadOnlyList<LibraryRootData> roots) : ILibraryScanRootPersistence, IVideoScanPersistence, IDownstreamNeedsPersistence, IImageGalleryScanPersistence, IAudioScanPersistence, IBookScanPersistence, IComicScanPersistence {
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
        public Exception? VideoUpsertException { get; init; }
        public IReadOnlyList<PlayableVideoSourceOwner> PlayableVideoSourceOwners { get; init; } = [];
        public IReadOnlyList<PlayableVideoRefreshSourceTarget> ExistingVideoTargets { get; init; } = [];
        public List<VideoSubtitleSidecarState> InvalidatedSubtitleStates { get; } = [];
        public List<(string PreviousPath, string ReplacementPath)> ReboundVideoPaths { get; } = [];
        public int PlayableVideoRecoveryTargetCalls { get; private set; }
        public int DownstreamNeedsChecks { get; private set; }
        public bool HasTechnical { get; init; }
        public IReadOnlyList<EntityRefreshTarget> ExistingAudioTrackTargets { get; init; } = [];
        public IReadOnlyDictionary<Guid, DownstreamNeeds> DownstreamNeedsById { get; init; } =
            new Dictionary<Guid, DownstreamNeeds>();
        public DownstreamNeeds? DefaultDownstreamNeeds { get; init; }
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
        public List<AudiobookBookRecord> UpsertedAudiobookBooks { get; } = [];
        public List<ComicSeriesRecord> UpsertedComicSeries { get; } = [];
        public List<ComicVolumeRecord> UpsertedComicVolumes { get; } = [];
        public List<ComicInstallmentRecord> UpsertedComicInstallments { get; } = [];
        public int GalleryBatchCalls { get; private set; }
        public int ImageBatchCalls { get; private set; }
        public int MusicArtistBatchCalls { get; private set; }
        public int AudioLibraryBatchCalls { get; private set; }
        public int AudioTrackBatchCalls { get; private set; }
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
        public IReadOnlyList<string> ValidComicArchivePaths { get; private set; } = [];
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

        public Task<Guid> UpsertImageAsync(string filePath, string title, Guid libraryRootId, Guid? galleryEntityId, long? sizeBytes, int sortOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"image:{filePath}");
            UpsertedImages.Add(new ImageRecord(id, filePath, title, libraryRootId, galleryEntityId, sortOrder));
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

        public Task<Guid> UpsertAudioTrackAsync(string filePath, string title, Guid libraryRootId, Guid? audioLibraryId, int sortOrder, string? sectionLabel, int sectionOrder, bool isNsfw, CancellationToken cancellationToken) {
            var id = IdFor($"audio-track:{filePath}");
            UpsertedAudioTracks.Add(new AudioTrackRecord(id, filePath, title, libraryRootId, audioLibraryId, sortOrder, sectionLabel, sectionOrder));
            return Task.FromResult(id);
        }

        public async Task<IReadOnlyList<Guid>> UpsertImagesBatchAsync(IReadOnlyList<ImageUpsertItem> items, CancellationToken cancellationToken) {
            ImageBatchCalls++;
            var ids = new List<Guid>(items.Count);
            foreach (var item in items) {
                ids.Add(await UpsertImageAsync(
                    item.FilePath,
                    item.Title,
                    item.LibraryRootId,
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
                    item.LibraryRootId,
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

        public Task<Guid> UpsertAudiobookBookAsync(string folderPath, string title, Guid libraryRootId, bool isNsfw, BookType bookType, BookFormat format, CancellationToken cancellationToken) {
            var id = IdFor($"book-series:{folderPath}");
            UpsertedAudiobookBooks.Add(new AudiobookBookRecord(id, folderPath, title, libraryRootId, bookType, format));
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

        public Task<Guid> UpsertComicSeriesAsync(
            string? folderPath,
            string title,
            Guid libraryRootId,
            bool isNsfw,
            CancellationToken cancellationToken) {
            var id = IdFor($"comic-series:{folderPath ?? title}");
            if (folderPath is not null) MarkOrganizedIfNeeded(id, folderPath);
            UpsertedComicSeries.Add(new ComicSeriesRecord(
                id, folderPath, title, libraryRootId, isNsfw));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertComicVolumeAsync(
            Guid seriesEntityId,
            string title,
            int volumeNumber,
            bool isNsfw,
            CancellationToken cancellationToken) {
            var id = IdFor($"comic-volume:{seriesEntityId}:{volumeNumber}");
            UpsertedComicVolumes.Add(new ComicVolumeRecord(
                id, seriesEntityId, title, volumeNumber, isNsfw));
            return Task.FromResult(id);
        }

        public Task<Guid> UpsertComicInstallmentAsync(
            string archivePath,
            string title,
            Guid libraryRootId,
            Guid parentEntityId,
            int sortOrder,
            int position,
            string positionLabel,
            ComicInstallmentKind installmentKind,
            long? sizeBytes,
            bool isNsfw,
            ComicSourceProvenance? sourceProvenance,
            CancellationToken cancellationToken) {
            var id = IdFor($"comic-installment:{archivePath}");
            UpsertedComicInstallments.Add(new ComicInstallmentRecord(
                id,
                archivePath,
                title,
                libraryRootId,
                parentEntityId,
                sortOrder,
                position,
                positionLabel,
                installmentKind,
                sizeBytes,
                isNsfw,
                sourceProvenance));
            return Task.FromResult(id);
        }

        public Task<int> RemoveStaleComicInstallmentsInRootAsync(
            Guid rootId,
            IReadOnlySet<string> validArchivePaths,
            CancellationToken cancellationToken) {
            ValidComicArchivePaths = validArchivePaths
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.FromResult(0);
        }

        public Task<int> RemoveEmptyComicContainersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> RemoveStalePlayableVideosByRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) =>
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
            return VideoUpsertException is null
                ? Task.FromResult(UpsertedVideoIds)
                : Task.FromException<IReadOnlyList<Guid>>(VideoUpsertException);
        }

        public Task<IReadOnlyList<Guid>> RebindPlayableVideoSourceAsync(
            string previousPath,
            string replacementPath,
            CancellationToken cancellationToken) {
            ReboundVideoPaths.Add((previousPath, replacementPath));
            return Task.FromResult(UpsertedVideoIds);
        }

        public Task<IReadOnlyList<PlayableVideoSourceOwner>> ListPlayableVideoSourceOwnersAsync(
            IReadOnlyCollection<string> filePaths,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlayableVideoSourceOwner>>(
                PlayableVideoSourceOwners
                    .Where(owner => filePaths.Contains(owner.FilePath, StringComparer.OrdinalIgnoreCase))
                    .ToArray());

        public Task InvalidateSubtitleStateAsync(
            IReadOnlyCollection<VideoSubtitleSidecarState> states,
            CancellationToken cancellationToken) {
            InvalidatedSubtitleStates.AddRange(states);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PlayableVideoRefreshSourceTarget>> GetPlayableVideoTargetsInRootAsync(
            Guid rootId,
            CancellationToken cancellationToken) => Task.FromResult(ExistingVideoTargets);

        public Task<IReadOnlyList<PlayableVideoRecoveryTarget>> GetPlayableVideoRecoveryTargetsInRootAsync(
            Guid rootId,
            CancellationToken cancellationToken) {
            PlayableVideoRecoveryTargetCalls++;
            return Task.FromResult<IReadOnlyList<PlayableVideoRecoveryTarget>>(ExistingVideoTargets
                .Where(target => DownstreamNeedsById.ContainsKey(target.Id))
                .Select(target => new PlayableVideoRecoveryTarget(
                    target.Id,
                    target.Title,
                    target.SourcePath,
                    DownstreamNeedsById[target.Id],
                    target.Kind))
                .ToArray());
        }

        public Task DiscardPendingScanChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<Guid, DownstreamNeeds>> CheckDownstreamNeedsBatchAsync(IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken) {
            DownstreamNeedsChecks++;
            IReadOnlyDictionary<Guid, DownstreamNeeds> needs = DefaultDownstreamNeeds is null
                ? DownstreamNeedsById
                : entityIds.ToDictionary(
                    id => id,
                    id => DownstreamNeedsById.GetValueOrDefault(id) ?? DefaultDownstreamNeeds!);
            return Task.FromResult(needs);
        }

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

    private sealed record ImageRecord(Guid Id, string FilePath, string Title, Guid LibraryRootId, Guid? GalleryEntityId, int SortOrder);
    private sealed record GalleryRecord(Guid Id, string FolderPath, string Title, Guid LibraryRootId, Guid? ParentGalleryEntityId, int SortOrder);
    private sealed record AudioTrackRecord(Guid Id, string FilePath, string Title, Guid LibraryRootId, Guid? AudioLibraryEntityId, int SortOrder, string? SectionLabel, int SectionOrder);
    private sealed record AudioLibraryRecord(Guid Id, string FolderPath, string Title, Guid LibraryRootId, Guid? ParentAudioLibraryEntityId, int SortOrder);
    private sealed record MusicArtistRecord(Guid Id, string FolderPath, string Title, Guid LibraryRootId, int SortOrder);
    private sealed record BookRecord(Guid Id, string SourcePath, string Title, Guid LibraryRootId, Guid? ParentEntityId, int? SortOrder);
    private sealed record AudiobookBookRecord(Guid Id, string SourcePath, string Title, Guid LibraryRootId, BookType BookType, BookFormat Format);
    private sealed record ComicSeriesRecord(
        Guid Id,
        string? FolderPath,
        string Title,
        Guid LibraryRootId,
        bool IsNsfw);
    private sealed record ComicVolumeRecord(
        Guid Id,
        Guid SeriesEntityId,
        string Title,
        int VolumeNumber,
        bool IsNsfw);
    private sealed record ComicInstallmentRecord(
        Guid Id,
        string ArchivePath,
        string Title,
        Guid LibraryRootId,
        Guid ParentEntityId,
        int SortOrder,
        int Position,
        string PositionLabel,
        ComicInstallmentKind InstallmentKind,
        long? SizeBytes,
        bool IsNsfw,
        ComicSourceProvenance? SourceProvenance);

    private sealed class RecordingComicFolderNormalizer(NormalizedComicArchive archive) : IComicFolderNormalizer {
        public IReadOnlyList<string> LastRetainedPaths { get; private set; } = [];

        public Task<ComicFolderNormalizationBatch> NormalizeAsync(
            LibraryRootData root,
            IReadOnlySet<string> excludedPaths,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ComicFolderNormalizationBatch([archive], []));

        public Task PruneAsync(
            Guid rootId,
            IReadOnlySet<string> retainedArchivePaths,
            CancellationToken cancellationToken) {
            LastRetainedPaths = retainedArchivePaths
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPageManifestStore : IEntityPageManifestStore {
        public List<EntityPageManifest> Manifests { get; } = [];

        public Task<bool> ReplaceAsync(
            EntityPageManifest manifest,
            CancellationToken cancellationToken) {
            Manifests.RemoveAll(existing => existing.EntityId == manifest.EntityId);
            Manifests.Add(manifest);
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(Manifests.RemoveAll(manifest => manifest.EntityId == entityId) > 0);
    }

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
        public List<AppliedBookFileMetadata> AppliedBooks { get; } = [];

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

        public Task ApplyBookFileMetadataAsync(
            Guid entityId,
            BookFileMetadata metadata,
            bool markNsfw,
            CancellationToken cancellationToken) {
            AppliedBooks.Add(new AppliedBookFileMetadata(entityId, metadata, markNsfw));
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

    private sealed record AppliedBookFileMetadata(
        Guid EntityId,
        BookFileMetadata Metadata,
        bool MarkNsfw);

    private sealed class NoopFileDiscovery : IFileDiscovery {
        public Task<IReadOnlyList<string>> DiscoverFilesAsync(string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FileSignature>> DiscoverFileSignaturesAsync(string rootPath, MediaCategory category, bool recursive, IReadOnlySet<string> excludedPaths, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileSignature>>([]);
    }

    private sealed class FixedAcquisitionHintApplier(Guid targetEntityId)
        : Prismedia.Application.Acquisition.IAcquisitionHintApplier {
        public Task<Guid?> ResolveTargetEntityIdAsync(
            EntityKind kind,
            Guid acquisitionId,
            CancellationToken cancellationToken) => Task.FromResult<Guid?>(targetEntityId);

        public Task<IReadOnlyList<Prismedia.Application.Acquisition.ImportedBookPathOwner>> ResolveImportedBookOwnersAsync(
            IReadOnlyCollection<string> sourcePaths,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Prismedia.Application.Acquisition.ImportedBookPathOwner>>(
                sourcePaths.Select(path =>
                    new Prismedia.Application.Acquisition.ImportedBookPathOwner(path, targetEntityId)).ToArray());

        public Task<bool> ApplyAsync(Guid entityId, string sourcePath, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> BindWantedFileAsync(
            EntityKind kind,
            string sourcePath,
            CancellationToken cancellationToken,
            Guid? acquisitionId = null,
            bool requireExactPath = false) => Task.FromResult(false);

        public Task<bool> BindWantedFolderAsync(
            EntityKind kind,
            string folderPath,
            CancellationToken cancellationToken,
            Guid? acquisitionId = null,
            bool requireExactPath = false) => Task.FromResult(false);

        public Task<bool> BindWantedParentFolderAsync(
            EntityKind parentKind,
            string folderPath,
            CancellationToken cancellationToken,
            Guid? acquisitionId = null) => Task.FromResult(false);

        public Task<Guid?> BindWantedChildFileBySortOrderAsync(
            EntityKind childKind,
            string parentPath,
            int sortOrder,
            string childPath,
            CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

        public Task<Guid?> BindWantedChildFolderBySortOrderAsync(
            EntityKind childKind,
            string parentPath,
            int sortOrder,
            string childPath,
            CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

        public Task<IReadOnlyList<Prismedia.Application.Acquisition.StampedHintOwner>> ApplyToFolderOwnersAsync(
            CancellationToken cancellationToken,
            Guid? acquisitionId = null) =>
            Task.FromResult<IReadOnlyList<Prismedia.Application.Acquisition.StampedHintOwner>>([]);
    }

    private sealed class CategoryFileDiscovery(
        IReadOnlyList<string> videos,
        IReadOnlyList<string> subtitles) : IFileDiscovery {
        public Task<IReadOnlyList<string>> DiscoverFilesAsync(
            string rootPath,
            MediaCategory category,
            bool recursive,
            IReadOnlySet<string> excludedPaths,
            CancellationToken cancellationToken) =>
            Task.FromResult(category == MediaCategory.Video ? videos : (IReadOnlyList<string>)[]);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(
            string rootPath,
            MediaCategory category,
            bool recursive,
            IReadOnlySet<string> excludedPaths,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FileSignature>> DiscoverFileSignaturesAsync(
            string rootPath,
            MediaCategory category,
            bool recursive,
            IReadOnlySet<string> excludedPaths,
            CancellationToken cancellationToken) {
            var paths = category switch {
                MediaCategory.Video => videos,
                MediaCategory.VideoSubtitleSidecar => subtitles,
                _ => []
            };
            return Task.FromResult<IReadOnlyList<FileSignature>>(
                paths.Select(path => new FileSignature(path, path.Length, 0)).ToArray());
        }
    }

    private sealed class FixedSubtitleSidecarDiscovery(VideoSubtitleSidecarDiscovery result)
        : ISubtitleSidecarDiscovery {
        public Task<IReadOnlyList<VideoSubtitleSidecarDiscovery>> DiscoverAsync(
            IReadOnlyCollection<string> videoPaths,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VideoSubtitleSidecarDiscovery>>([result]);
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

    private sealed class ChangedSignatureFileDiscovery(
        string path,
        IReadOnlyList<string>? unchangedPaths = null) : IFileDiscovery {
        public int DetailedDiscoveryCalls { get; private set; }

        public Task<IReadOnlyList<string>> DiscoverFilesAsync(
            string rootPath,
            MediaCategory category,
            bool recursive,
            IReadOnlySet<string> excludedPaths,
            CancellationToken cancellationToken) {
            DetailedDiscoveryCalls++;
            return Task.FromResult<IReadOnlyList<string>>(category == MediaCategory.Video
                ? [path, .. (unchangedPaths ?? [])]
                : []);
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(
            string rootPath,
            MediaCategory category,
            bool recursive,
            IReadOnlySet<string> excludedPaths,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FileSignature>> DiscoverFileSignaturesAsync(
            string rootPath,
            MediaCategory category,
            bool recursive,
            IReadOnlySet<string> excludedPaths,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileSignature>>(category == MediaCategory.Video
                ? [
                    new FileSignature(path, path.Length + 1, 1),
                    .. (unchangedPaths ?? []).Select(unchanged =>
                        new FileSignature(unchanged, unchanged.Length, 0))
                ]
                : []);
    }

    private sealed class ScopedChangeFileDiscovery(string changedPath, string untouchedPath) : IFileDiscovery {
        public int DetailedDiscoveryCalls { get; private set; }
        public List<string> SignatureRoots { get; } = [];

        public Task<IReadOnlyList<string>> DiscoverFilesAsync(
            string rootPath,
            MediaCategory category,
            bool recursive,
            IReadOnlySet<string> excludedPaths,
            CancellationToken cancellationToken) {
            DetailedDiscoveryCalls++;
            return Task.FromResult<IReadOnlyList<string>>([changedPath, untouchedPath]);
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverFilesByDirectoryAsync(
            string rootPath,
            MediaCategory category,
            bool recursive,
            IReadOnlySet<string> excludedPaths,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<FileSignature>> DiscoverFileSignaturesAsync(
            string rootPath,
            MediaCategory category,
            bool recursive,
            IReadOnlySet<string> excludedPaths,
            CancellationToken cancellationToken) {
            SignatureRoots.Add(rootPath);
            return Task.FromResult<IReadOnlyList<FileSignature>>(category == MediaCategory.Video
                ? [
                    new FileSignature(changedPath, changedPath.Length + 10, 1),
                    new FileSignature(untouchedPath, untouchedPath.Length, 0)
                ]
                : []);
        }
    }

    private sealed class FixedChangeIntake(string path) : ILibraryFileChangeIntake {
        public int CompletionCount { get; private set; }

        public Task RecordAsync(
            Guid rootId,
            string scanKind,
            IReadOnlyCollection<string> absolutePaths,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<LibraryFileChangeBatch> LoadAsync(
            Guid rootId,
            string scanKind,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LibraryFileChangeBatch([path], DateTimeOffset.UtcNow));

        public Task<bool> HasPendingAsync(Guid rootId, string scanKind, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task CompleteAsync(
            Guid rootId,
            string scanKind,
            IReadOnlyCollection<string> absolutePaths,
            DateTimeOffset observedThrough,
            CancellationToken cancellationToken) {
            CompletionCount++;
            return Task.CompletedTask;
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
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) => Task.FromResult<JobRunSnapshot?>(null);
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
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) => Task.FromResult<JobRunSnapshot?>(null);
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
