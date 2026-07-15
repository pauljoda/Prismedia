using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Identity;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Subtitles;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class ExtractSubtitlesJobHandlerTests : IDisposable {
    private readonly Guid _entityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"prismedia-extract-subtitles-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReliableSidecarsAreReconciledWithDuplicateUndeterminedLanguages() {
        var videoPath = CreateFile("Movie.mkv");
        var first = Candidate("Movie.commentary.srt", "sidecar:first", SubtitleLanguages.Undetermined, "Commentary");
        var second = Candidate("Movie.forced.srt", "sidecar:second", SubtitleLanguages.Undetermined, "Forced");
        var discovery = new RecordingSidecarDiscovery(
            new VideoSubtitleSidecarDiscovery(videoPath, [first, second], "reliable-signature", IsComplete: true));
        var assets = new RecordingSubtitleAssets {
            Import = (_, inputPath, sourceKey, _, _) => Task.FromResult(
                new ImportedSidecarSubtitleAssets(
                    Path.Combine(_tempDirectory, $"{sourceKey}.vtt"),
                    SourcePath: null,
                    CreatedPaths: [Path.Combine(_tempDirectory, $"{Path.GetFileName(inputPath)}.vtt")]))
        };
        var persistence = new RecordingSubtitlePersistence(videoPath);
        var handler = CreateHandler(new RecordingMediaProbe([]), assets, persistence, discovery);

        await handler.HandleAsync(Context(), CancellationToken.None);

        var reconciliation = Assert.Single(persistence.Reconciliations);
        Assert.Equal("reliable-signature", reconciliation.Signature);
        Assert.Equal(2, reconciliation.Tracks.Count);
        Assert.All(reconciliation.Tracks, track =>
            Assert.Equal(SubtitleLanguages.Undetermined, track.Language));
        Assert.Equal(
            ["sidecar:first", "sidecar:second"],
            reconciliation.Tracks.Select(track => track.SourceKey).Order().ToArray());
        Assert.Equal(0, persistence.MarkSubtitlesExtractedCalls);
    }

    [Fact]
    public async Task IncompleteDiscoveryThrowsBeforeAssetsOrPersistenceAreTouched() {
        var videoPath = CreateFile("Movie.mkv");
        var candidate = Candidate("Movie.en.srt", "sidecar:english", "en", null);
        var discovery = new RecordingSidecarDiscovery(
            new VideoSubtitleSidecarDiscovery(videoPath, [candidate], "partial-signature", IsComplete: false));
        var probe = new RecordingMediaProbe([]);
        var assets = new RecordingSubtitleAssets();
        var persistence = new RecordingSubtitlePersistence(videoPath);
        var handler = CreateHandler(probe, assets, persistence, discovery);

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            handler.HandleAsync(Context(), CancellationToken.None));

        Assert.Contains("could not read", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, probe.ProbeSubtitleCalls);
        Assert.Empty(assets.ImportCalls);
        Assert.Empty(assets.DeletedPathSets);
        Assert.Empty(persistence.Reconciliations);
        Assert.Equal(0, persistence.MarkSubtitlesExtractedCalls);
    }

    [Fact]
    public async Task MissingSourceDoesNotMarkSubtitleReconciliationComplete() {
        var missingVideoPath = Path.Combine(_tempDirectory, "Missing.mkv");
        var discovery = new RecordingSidecarDiscovery(
            new VideoSubtitleSidecarDiscovery(
                missingVideoPath,
                [],
                new string('0', 64),
                IsComplete: true));
        var probe = new RecordingMediaProbe([]);
        var assets = new RecordingSubtitleAssets();
        var persistence = new RecordingSubtitlePersistence(missingVideoPath);
        var handler = CreateHandler(probe, assets, persistence, discovery);

        await handler.HandleAsync(Context(), CancellationToken.None);

        Assert.Equal(0, persistence.MarkSubtitlesExtractedCalls);
        Assert.Equal(0, probe.ProbeSubtitleCalls);
        Assert.Empty(assets.ImportCalls);
        Assert.Empty(persistence.Reconciliations);
    }

    [Fact]
    public async Task FailedSidecarImportReconcilesValidTracksAsIncompleteAndCanRecover() {
        var videoPath = CreateFile("Movie.mkv");
        var first = Candidate("Movie.en.srt", "sidecar:english", "en", null);
        var second = Candidate("Movie.fr.srt", "sidecar:french", "fr", null);
        var fullSignature = new string('a', 64);
        var discovery = new RecordingSidecarDiscovery(
            new VideoSubtitleSidecarDiscovery(videoPath, [first, second], fullSignature, IsComplete: true));
        var newlyCreated = Path.Combine(_tempDirectory, "new-english.vtt");
        var recoveredFrench = Path.Combine(_tempDirectory, "recovered-french.vtt");
        var embeddedPath = Path.Combine(_tempDirectory, "embedded-4.vtt");
        var frenchAttempts = 0;
        var assets = new RecordingSubtitleAssets {
            EmbeddedPaths = [embeddedPath],
            Import = (_, _, sourceKey, _, _) => sourceKey switch {
                "sidecar:english" => Task.FromResult(new ImportedSidecarSubtitleAssets(
                    newlyCreated,
                    SourcePath: null,
                    CreatedPaths: [newlyCreated])),
                _ when frenchAttempts++ == 0 => Task.FromException<ImportedSidecarSubtitleAssets>(
                    new SubtitleAssetImportException("conversion failed")),
                _ => Task.FromResult(new ImportedSidecarSubtitleAssets(
                    recoveredFrench,
                    SourcePath: null,
                    CreatedPaths: [recoveredFrench]))
            }
        };
        var persistence = new RecordingSubtitlePersistence(videoPath);
        var handler = CreateHandler(
            new RecordingMediaProbe([new SubtitleStreamData(4, "subrip", "en", "Embedded")]),
            assets,
            persistence,
            discovery);

        await handler.HandleAsync(Context(), CancellationToken.None);

        var partial = Assert.Single(persistence.Reconciliations);
        Assert.False(partial.IsComplete);
        Assert.Equal(fullSignature, partial.Signature);
        Assert.Equal(
            ["sidecar:english", "stream:4"],
            partial.Tracks.Select(track => track.SourceKey).Order().ToArray());
        Assert.DoesNotContain(partial.Tracks, track => track.SourceKey == "sidecar:french");
        Assert.DoesNotContain(assets.DeletedPathSets.SelectMany(paths => paths), path => path == newlyCreated);

        await handler.HandleAsync(Context(), CancellationToken.None);

        var recovered = persistence.Reconciliations[1];
        Assert.True(recovered.IsComplete);
        Assert.Equal(fullSignature, recovered.Signature);
        Assert.Equal(
            ["sidecar:english", "sidecar:french", "stream:4"],
            recovered.Tracks.Select(track => track.SourceKey).Order().ToArray());
    }

    [Fact]
    public async Task SuccessfulReconciliationDeletesOnlyObsoleteAssets() {
        var videoPath = CreateFile("Movie.mkv");
        var obsoleteVtt = Path.Combine(_tempDirectory, "obsolete.vtt");
        var obsoleteAss = Path.Combine(_tempDirectory, "obsolete.ass");
        var retained = Path.Combine(_tempDirectory, "retained.vtt");
        var discovery = new RecordingSidecarDiscovery(
            new VideoSubtitleSidecarDiscovery(videoPath, [], "empty-signature", IsComplete: true));
        var assets = new RecordingSubtitleAssets();
        var persistence = new RecordingSubtitlePersistence(videoPath) {
            ReconciliationResult = new SubtitleReconciliationResult(
                RetainedAssetPaths: [retained],
                ObsoleteAssetPaths: [obsoleteVtt, obsoleteAss])
        };
        var handler = CreateHandler(new RecordingMediaProbe([]), assets, persistence, discovery);

        await handler.HandleAsync(Context(), CancellationToken.None);

        var deleted = Assert.Single(assets.DeletedPathSets);
        Assert.Equal([obsoleteVtt, obsoleteAss], deleted);
        Assert.DoesNotContain(retained, deleted);
        Assert.Single(persistence.Reconciliations);
        Assert.Equal(0, persistence.MarkSubtitlesExtractedCalls);
    }

    [Fact]
    public async Task CompleteReconciliationSchedulesAutomaticAcquisitionAfterCommit() {
        var videoPath = CreateFile("Movie.mkv");
        var discovery = new RecordingSidecarDiscovery(
            new VideoSubtitleSidecarDiscovery(videoPath, [], "complete-signature", IsComplete: true));
        var persistence = new RecordingSubtitlePersistence(videoPath);
        var scheduler = new RecordingAcquisitionScheduler();
        var handler = CreateHandler(
            new RecordingMediaProbe([]),
            new RecordingSubtitleAssets(),
            persistence,
            discovery,
            scheduler);

        await handler.HandleAsync(Context(), CancellationToken.None);

        var scheduled = Assert.Single(scheduler.Requests);
        Assert.Equal(_entityId, scheduled.VideoId);
        Assert.Equal("Movie", scheduled.Label);
        Assert.Single(persistence.Reconciliations);
    }

    [Fact]
    public async Task PostCommitCleanupFailureNeverDeletesNewlyCommittedAssets() {
        var videoPath = CreateFile("Movie.mkv");
        var candidate = Candidate("Movie.en.srt", "sidecar:english", "en", null);
        var discovery = new RecordingSidecarDiscovery(
            new VideoSubtitleSidecarDiscovery(videoPath, [candidate], "committed-signature", IsComplete: true));
        var created = Path.Combine(_tempDirectory, "created.vtt");
        var obsolete = Path.Combine(_tempDirectory, "obsolete.vtt");
        var assets = new RecordingSubtitleAssets {
            Import = (_, _, _, _, _) => Task.FromResult(
                new ImportedSidecarSubtitleAssets(created, null, CreatedPaths: [created])),
            DeleteException = new IOException("cleanup failed")
        };
        var persistence = new RecordingSubtitlePersistence(videoPath) {
            ReconciliationResult = new SubtitleReconciliationResult([created], [obsolete])
        };
        var handler = CreateHandler(new RecordingMediaProbe([]), assets, persistence, discovery);

        await handler.HandleAsync(Context(), CancellationToken.None);

        Assert.Single(persistence.Reconciliations);
        var attemptedCleanup = Assert.Single(assets.DeletedPathSets);
        Assert.Equal([obsolete], attemptedCleanup);
        Assert.DoesNotContain(created, attemptedCleanup);
    }

    [Fact]
    public async Task ReconciliationFailureDoesNotDeleteAssetsFromAnAmbiguousCommit() {
        var videoPath = CreateFile("Movie.mkv");
        var candidate = Candidate("Movie.en.srt", "sidecar:english", "en", null);
        var discovery = new RecordingSidecarDiscovery(
            new VideoSubtitleSidecarDiscovery(videoPath, [candidate], new string('c', 64), IsComplete: true));
        var created = Path.Combine(_tempDirectory, "possibly-committed.vtt");
        var assets = new RecordingSubtitleAssets {
            Import = (_, _, _, _, _) => Task.FromResult(
                new ImportedSidecarSubtitleAssets(created, null, CreatedPaths: [created]))
        };
        var persistence = new RecordingSubtitlePersistence(videoPath) {
            ReconciliationException = new IOException("connection dropped after commit")
        };
        var handler = CreateHandler(new RecordingMediaProbe([]), assets, persistence, discovery);

        await Assert.ThrowsAsync<IOException>(() =>
            handler.HandleAsync(Context(), CancellationToken.None));

        Assert.Single(persistence.Reconciliations);
        Assert.Empty(assets.DeletedPathSets);
    }

    [Fact]
    public async Task CompleteManifestUsesStreamIndexAndCandidateSourceKeys() {
        var videoPath = CreateFile("Movie.mkv");
        var candidate = Candidate("Movie.en.ass", "sidecar:styled", "en", "Signs");
        var discovery = new RecordingSidecarDiscovery(
            new VideoSubtitleSidecarDiscovery(videoPath, [candidate], "combined-signature", IsComplete: true));
        var embeddedPath = Path.Combine(_tempDirectory, "embedded-7.vtt");
        var sidecarVtt = Path.Combine(_tempDirectory, "sidecar.vtt");
        var sidecarAss = Path.Combine(_tempDirectory, "sidecar.ass");
        var assets = new RecordingSubtitleAssets {
            EmbeddedPaths = [embeddedPath],
            Import = (_, _, _, _, _) => Task.FromResult(new ImportedSidecarSubtitleAssets(
                sidecarVtt,
                sidecarAss,
                CreatedPaths: [sidecarVtt, sidecarAss]))
        };
        var probe = new RecordingMediaProbe([
            new SubtitleStreamData(7, "subrip", "en", "Embedded English")
        ]);
        var persistence = new RecordingSubtitlePersistence(videoPath);
        var handler = CreateHandler(probe, assets, persistence, discovery);

        await handler.HandleAsync(Context(), CancellationToken.None);

        var reconciliation = Assert.Single(persistence.Reconciliations);
        Assert.Equal("combined-signature", reconciliation.Signature);
        var embedded = Assert.Single(reconciliation.Tracks, track =>
            track.Source == EntitySubtitleSource.Embedded);
        Assert.Equal("stream:7", embedded.SourceKey);
        Assert.Equal("7", embedded.SourcePath);
        Assert.Equal(embeddedPath, embedded.StoragePath);

        var sidecar = Assert.Single(reconciliation.Tracks, track =>
            track.Source == EntitySubtitleSource.Sidecar);
        Assert.Equal(candidate.SourceKey, sidecar.SourceKey);
        Assert.Equal(sidecarVtt, sidecar.StoragePath);
        Assert.Equal(sidecarAss, sidecar.SourcePath);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDirectory)) {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private ExtractSubtitlesJobHandler CreateHandler(
        IMediaProbe mediaProbe,
        ISubtitleAssetService assets,
        IMediaProcessingStatePersistence persistence,
        ISubtitleSidecarDiscovery discovery,
        IAutomaticSubtitleAcquisitionScheduler? acquisitionScheduler = null) =>
        new(
            NullLogger<ExtractSubtitlesJobHandler>.Instance,
            mediaProbe,
            assets,
            persistence,
            discovery,
            acquisitionScheduler);

    private JobContext Context() => new(
        new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.ExtractSubtitles,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: "{}",
            TargetEntityKind: EntityKindRegistry.Video.Code,
            TargetEntityId: _entityId.ToString(),
            TargetLabel: "Movie",
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null),
        new NoopJobQueue());

    private string CreateFile(string name) {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(path, "video");
        return path;
    }

    private SubtitleSidecarCandidate Candidate(
        string fileName,
        string sourceKey,
        string language,
        string? label) =>
        new(
            Path.Combine(_tempDirectory, fileName),
            sourceKey,
            Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant(),
            language,
            label,
            SizeBytes: 12,
            ModifiedTicks: 34);

    private sealed class RecordingSidecarDiscovery(VideoSubtitleSidecarDiscovery result)
        : ISubtitleSidecarDiscovery {
        public Task<IReadOnlyList<VideoSubtitleSidecarDiscovery>> DiscoverAsync(
            IReadOnlyCollection<string> videoPaths,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VideoSubtitleSidecarDiscovery>>([result]);
    }

    private sealed class RecordingAcquisitionScheduler : IAutomaticSubtitleAcquisitionScheduler {
        public List<(Guid VideoId, string Label)> Requests { get; } = [];

        public Task ScheduleAsync(Guid videoId, string label, CancellationToken cancellationToken) {
            Requests.Add((videoId, label));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingMediaProbe(IReadOnlyList<SubtitleStreamData> streams) : IMediaProbe {
        public int ProbeSubtitleCalls { get; private set; }

        public Task<IReadOnlyList<SubtitleStreamData>> ProbeSubtitleStreamsAsync(
            string filePath,
            CancellationToken cancellationToken) {
            ProbeSubtitleCalls++;
            return Task.FromResult(streams);
        }

        public Task<VideoProbeData?> ProbeVideoAsync(string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AudioProbeData?> ProbeAudioAsync(string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ImageProbeData?> ProbeImageAsync(string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed record ReconciliationCall(
        Guid EntityId,
        string Signature,
        IReadOnlyList<ManagedSubtitleTrackData> Tracks,
        bool IsComplete);

    private sealed class RecordingSubtitlePersistence(string sourcePath)
        : IMediaProcessingStatePersistence {
        public List<ReconciliationCall> Reconciliations { get; } = [];
        public int MarkSubtitlesExtractedCalls { get; private set; }
        public SubtitleReconciliationResult ReconciliationResult { get; init; } = new([], []);
        public Exception? ReconciliationException { get; init; }

        public Task<string?> GetSourceFilePathAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(sourcePath);

        public Task MarkSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) {
            MarkSubtitlesExtractedCalls++;
            return Task.CompletedTask;
        }

        public Task<SubtitleReconciliationResult> ReconcileManagedSubtitlesAsync(
            Guid entityId,
            string sidecarSignature,
            IReadOnlyList<ManagedSubtitleTrackData> tracks,
            bool isComplete,
            CancellationToken cancellationToken) {
            Reconciliations.Add(new ReconciliationCall(entityId, sidecarSignature, tracks.ToArray(), isComplete));
            return ReconciliationException is null
                ? Task.FromResult(ReconciliationResult)
                : Task.FromException<SubtitleReconciliationResult>(ReconciliationException);
        }

        public Task UpsertEntityTechnicalAsync(Guid entityId, double? duration, int? width, int? height,
            double? frameRate, int? bitRate, int? sampleRate, int? channels, string? codec,
            string? container, string? format, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertMediaSourceAsync(Guid entityId, string path, MediaSourceProbeData source,
            IReadOnlyList<MediaStreamProbeData> streams, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertTrickplayInfoAsync(Guid entityId, TrickplayInfoData info,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpsertEntityFileAsync(Guid entityId, EntityFileRole role, string path,
            string? mimeType, long? sizeBytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm,
            string value, Guid? entityFileId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid?> GetSourceFileIdAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertSubtitleAsync(Guid entityId, string language, string? label, string format,
            EntitySubtitleSource source, string storagePath, string sourceFormat, int streamIndex,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpsertAudioTrackTagsAsync(Guid entityId, string? artist, string? album,
            int? trackNumber, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<EntityTechnicalData?> GetEntityTechnicalAsync(Guid entityId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkEntityProbeFailedAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ClearProbeFailuresForPathsAsync(IReadOnlyCollection<string> sourcePaths,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed record ImportCall(
        Guid EntityId,
        string InputPath,
        string SourceKey,
        string SourceFormat);

    private sealed class RecordingSubtitleAssets : ISubtitleAssetService {
        public Func<Guid, string, string, string, CancellationToken, Task<ImportedSidecarSubtitleAssets>> Import {
            get;
            init;
        } = (_, _, _, _, _) => Task.FromResult(
            new ImportedSidecarSubtitleAssets("/cache/sidecar.vtt", null, ["/cache/sidecar.vtt"]));

        public IReadOnlyList<string> EmbeddedPaths { get; init; } = [];
        public Exception? DeleteException { get; init; }
        public List<ImportCall> ImportCalls { get; } = [];
        public List<IReadOnlyList<string>> DeletedPathSets { get; } = [];

        public Task<IReadOnlyList<string>> ExtractSubtitlesAsync(
            Guid entityId,
            string inputPath,
            IReadOnlyList<SubtitleStreamData> streams,
            CancellationToken cancellationToken) => Task.FromResult(EmbeddedPaths);

        public async Task<ImportedSidecarSubtitleAssets> ImportSidecarSubtitleAsync(
            Guid entityId,
            string inputPath,
            string sourceKey,
            string sourceFormat,
            CancellationToken cancellationToken) {
            ImportCalls.Add(new ImportCall(entityId, inputPath, sourceKey, sourceFormat));
            return await Import(entityId, inputPath, sourceKey, sourceFormat, cancellationToken);
        }

        public Task DeleteSubtitleAssetsAsync(
            IReadOnlyCollection<string> paths,
            CancellationToken cancellationToken) {
            DeletedPathSets.Add(paths.ToArray());
            return DeleteException is null
                ? Task.CompletedTask
                : Task.FromException(DeleteException);
        }

    }

    private sealed class NoopJobQueue : IJobQueueService {
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests,
            CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken,
            JobRunLane? lane = null) => Task.FromResult<JobRunSnapshot?>(null);

        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter,
            CancellationToken cancellationToken) => Task.FromResult(0);

        public Task UpdateProgressAsync(Guid id, int progress, string? message,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task FailAsync(Guid id, string message, TimeSpan retryDelay,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobQueueCount>>([]);

        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
