using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Identity;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class ExtractSubtitlesJobHandlerTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-subtitle-handler-{Guid.NewGuid():N}");

    public ExtractSubtitlesJobHandlerTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task RecordsExtractedSubtitleFilePath() {
        var entityId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var sourcePath = Path.Combine(_tempDir, "movie.mkv");
        await File.WriteAllTextAsync(sourcePath, "video");
        var subtitlePath = Path.Combine(_tempDir, "subtitles", "embedded-eng-3.vtt");
        var assets = new RecordingAssetGenerator(_tempDir, subtitlePath);
        var persistence = new RecordingScanPersistence(sourcePath);
        var handler = new ExtractSubtitlesJobHandler(
            NullLogger<ExtractSubtitlesJobHandler>.Instance,
            new RecordingMediaProbe(),
            assets,
            persistence);

        await handler.HandleAsync(new JobContext(CreateJob(entityId), new NoopJobQueue()), CancellationToken.None);

        var subtitle = Assert.Single(persistence.Subtitles);
        Assert.Equal(subtitlePath, subtitle.StoragePath);
        Assert.Equal("eng", subtitle.Language);
        Assert.Equal("English", subtitle.Label);
        Assert.True(persistence.MarkedExtracted);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static JobRunSnapshot CreateJob(Guid entityId) => new(
        Guid.NewGuid(),
        JobType.ExtractSubtitles,
        JobRunStatus.Running,
        Progress: 0,
        Message: null,
        PayloadJson: "{}",
        TargetEntityKind: "video",
        TargetEntityId: entityId.ToString(),
        TargetLabel: "Movie",
        CreatedAt: DateTimeOffset.UtcNow,
        StartedAt: DateTimeOffset.UtcNow,
        FinishedAt: null);

    private sealed class RecordingMediaProbe : IMediaProbe {
        public Task<IReadOnlyList<SubtitleStreamData>> ProbeSubtitleStreamsAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubtitleStreamData>>([
                new SubtitleStreamData(3, "subrip", "eng", "English")
            ]);

        public Task<VideoProbeData?> ProbeVideoAsync(string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AudioProbeData?> ProbeAudioAsync(string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ImageProbeData?> ProbeImageAsync(string filePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAssetGenerator(string tempDir, string subtitlePath) : IMediaAssetGenerator {
        public async Task<IReadOnlyList<string>> ExtractSubtitlesAsync(
            string inputPath,
            string outputDir,
            IReadOnlyList<SubtitleStreamData> streams,
            CancellationToken cancellationToken) {
            Directory.CreateDirectory(Path.GetDirectoryName(subtitlePath)!);
            await File.WriteAllTextAsync(subtitlePath, "WEBVTT", cancellationToken);
            return [subtitlePath];
        }

        public string SubtitleDir(Guid entityId) => Path.Combine(tempDir, "subtitles");
        public string SubtitleUrl(Guid entityId, string fileName) => throw new NotSupportedException("Subtitle extraction should store file paths, not asset URLs.");

        public Task<bool> GenerateVideoThumbnailAsync(string inputPath, string outputPath, double seekSeconds, int width, int height, int quality, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GeneratePreviewClipAsync(string inputPath, string outputPath, double startSeconds, int durationSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExtractTrickplayFrameAsync(string inputPath, string outputPath, double seekSeconds, int width, int height, int jpegQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ExtractTrickplayFramesBatchAsync(string inputPath, string outputDir, double duration, int intervalSeconds, int width, int height, int jpegQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ComposeSpriteSheetAsync(string frameDir, string outputPath, int columns, int frameWidth, int frameHeight, int jpegQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ComposeTiledJpegSheetsAsync(string frameDir, string outputDir, int columns, int rows, int frameWidth, int frameHeight, int jpegQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(bool Thumbnail, bool Preview)> GenerateThumbnailAndPreviewAsync(string inputPath, string thumbnailPath, double thumbSeekSeconds, int thumbWidth, int thumbHeight, int thumbQuality, string previewPath, double previewStartSeconds, int previewDurationSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GenerateImageThumbnailAsync(string inputPath, string outputPath, int targetWidth, int quality, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int[]?> GenerateWaveformDataAsync(string inputPath, double durationSeconds, int pixelsPerSecond, CancellationToken cancellationToken) => throw new NotSupportedException();
        public string VideoThumbnailPath(Guid entityId) => throw new NotSupportedException();
        public string VideoPreviewPath(Guid entityId) => throw new NotSupportedException();
        public string VideoSpritePath(Guid entityId) => throw new NotSupportedException();
        public string VideoTrickplayVttPath(Guid entityId) => throw new NotSupportedException();
        public string TrickplayFrameDir(Guid entityId) => throw new NotSupportedException();
        public string TrickplayTileDir(Guid entityId, int width) => throw new NotSupportedException();
        public string ImageThumbnailPath(Guid entityId) => throw new NotSupportedException();
        public string BookPageThumbnailPath(Guid entityId) => throw new NotSupportedException();
        public string AudioWaveformPath(Guid entityId) => throw new NotSupportedException();
        public string VideoThumbnailUrl(Guid entityId) => throw new NotSupportedException();
        public string VideoPreviewUrl(Guid entityId) => throw new NotSupportedException();
        public string VideoTrickplayVttUrl(Guid entityId) => throw new NotSupportedException();
        public string TrickplayPlaylistUrl(Guid entityId, int width) => throw new NotSupportedException();
        public string ImageThumbnailUrl(Guid entityId) => throw new NotSupportedException();
        public string BookPageThumbnailUrl(Guid entityId) => throw new NotSupportedException();
        public string AudioWaveformUrl(Guid entityId) => throw new NotSupportedException();
    }

    private sealed class RecordingScanPersistence(string sourcePath) : ILibraryScanPersistence {
        public List<RecordedSubtitle> Subtitles { get; } = [];
        public bool MarkedExtracted { get; private set; }

        public Task<string?> GetSourceFilePathAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(sourcePath);

        public Task MarkSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) {
            MarkedExtracted = true;
            return Task.CompletedTask;
        }

        public Task UpsertSubtitleAsync(Guid entityId, string language, string? label, string format, EntitySubtitleSource source, string storagePath, string sourceFormat, int streamIndex, CancellationToken cancellationToken) {
            Subtitles.Add(new RecordedSubtitle(language, label, storagePath));
            return Task.CompletedTask;
        }

        public Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertVideoAsync(string filePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertImageAsync(string filePath, string title, Guid? galleryEntityId, long? sizeBytes, int sortOrder, bool isNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertGalleryAsync(string folderPath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertGalleryAsync(string folderPath, string title, Guid libraryRootId, Guid? parentGalleryEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertAudioTrackAsync(string filePath, string title, Guid audioLibraryId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertAudioTrackAsync(string filePath, string title, Guid? audioLibraryId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertAudioLibraryAsync(string folderPath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertAudioLibraryAsync(string folderPath, string title, Guid libraryRootId, Guid? parentAudioLibraryEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertBookAsync(string sourcePath, string title, Guid libraryRootId, bool isNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertBookVolumeAsync(string folderPath, string title, Guid bookEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertBookChapterAsync(string archivePath, string title, Guid parentEntityId, int sortOrder, int pageCount, bool isNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertBookPageAsync(string filePath, string title, Guid bookEntityId, Guid chapterEntityId, int sortOrder, bool isNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveStaleVideosByRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveStaleLooseImagesInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveStaleImagesInGalleryAsync(Guid galleryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveStaleGalleriesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveStaleLooseAudioTracksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveStaleAudioTracksInLibraryAsync(Guid libraryEntityId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveStaleAudioLibrariesInRootAsync(Guid rootId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveStaleBookVolumesAsync(Guid bookEntityId, IReadOnlySet<string> validFolderPaths, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveStaleBookChaptersAsync(Guid bookEntityId, IReadOnlySet<string> validArchivePaths, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveStaleBooksInRootAsync(Guid rootId, IReadOnlySet<string> validPaths, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveOrphanSeriesAndSeasonsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> UpsertVideosBatchAsync(IReadOnlyList<VideoUpsertItem> items, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, DownstreamNeeds>> CheckDownstreamNeedsBatchAsync(IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasEntityFileAsync(Guid entityId, EntityFileRole role, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertEntityTechnicalAsync(Guid entityId, double? duration, int? width, int? height, double? frameRate, int? bitRate, int? sampleRate, int? channels, string? codec, string? container, string? format, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertMediaSourceAsync(Guid entityId, string path, MediaSourceProbeData source, IReadOnlyList<MediaStreamProbeData> streams, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertTrickplayInfoAsync(Guid entityId, TrickplayInfoData info, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertEntityFileAsync(Guid entityId, EntityFileRole role, string path, string? mimeType, long? sizeBytes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, string value, Guid? entityFileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> GetSourceFileIdAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertAudioTrackTagsAsync(Guid entityId, string? artist, string? album, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityTechnicalData?> GetEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<EntityRefreshTarget>> GetEntityTreeAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed record RecordedSubtitle(string Language, string? Label, string StoragePath);

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
}
