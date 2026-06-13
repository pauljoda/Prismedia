using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Generate;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class GenerateImageThumbnailJobHandlerTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-image-preview-handler-{Guid.NewGuid():N}");

    public GenerateImageThumbnailJobHandlerTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task VideoLikeGalleryImageCreatesThumbnailAndPreviewClip() {
        var entityId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var sourcePath = Path.Combine(_tempDir, "animated.webm");
        await File.WriteAllTextAsync(sourcePath, "video-ish image");
        var assets = new RecordingMediaAssetGenerator(_tempDir);
        var persistence = new ImagePreviewPersistence(sourcePath) {
            Settings = DefaultSettings with { PreviewClipDurationSeconds = 6 }
        };
        var handler = CreateHandler(assets, persistence);

        await handler.HandleAsync(new JobContext(Job(entityId, "Animated"), new NoopJobQueue()), CancellationToken.None);

        Assert.True(assets.GeneratedPreviewClip);
        Assert.Equal(6, assets.PreviewDurationSeconds);
        Assert.Contains(persistence.EntityFiles, file =>
            file.Role == EntityFileRole.Thumbnail &&
            file.Path == $"/assets/images/{entityId}/thumb.jpg" &&
            file.MimeType == MediaContentTypes.ImageJpeg);
        Assert.Contains(persistence.EntityFiles, file =>
            file.Role == EntityFileRole.Preview &&
            file.Path == $"/assets/images/{entityId}/preview.mp4" &&
            file.MimeType == MediaContentTypes.VideoMp4);
    }

    [Fact]
    public async Task StillGalleryImageCreatesOnlyThumbnail() {
        var entityId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var sourcePath = Path.Combine(_tempDir, "photo.jpg");
        await File.WriteAllTextAsync(sourcePath, "still image");
        var assets = new RecordingMediaAssetGenerator(_tempDir);
        var persistence = new ImagePreviewPersistence(sourcePath);
        var handler = CreateHandler(assets, persistence);

        await handler.HandleAsync(new JobContext(Job(entityId, "Photo"), new NoopJobQueue()), CancellationToken.None);

        Assert.False(assets.GeneratedPreviewClip);
        Assert.Contains(persistence.EntityFiles, file => file.Role == EntityFileRole.Thumbnail);
        Assert.DoesNotContain(persistence.EntityFiles, file => file.Role == EntityFileRole.Preview);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static readonly LibrarySettingsData DefaultSettings = new(
        AutoGenerateMetadata: false,
        AutoGenerateOshash: false,
        AutoGenerateMd5: false,
        AutoGeneratePreview: true,
        GenerateTrickplay: false,
        TrickplayIntervalSeconds: 10,
        PreviewClipDurationSeconds: 8,
        ThumbnailQuality: 2,
        TrickplayQuality: 2);

    private static GenerateImageThumbnailJobHandler CreateHandler(
        RecordingMediaAssetGenerator assets,
        ImagePreviewPersistence persistence) =>
        new(
            NullLogger<GenerateImageThumbnailJobHandler>.Instance,
            assets,
            new RecordingImageThumbnailGenerator(),
            persistence,
            persistence);

    private static JobRunSnapshot Job(Guid entityId, string label) =>
        new(
            Guid.NewGuid(),
            JobType.GenerateImageThumbnail,
            JobRunStatus.Running,
            Progress: 0,
            Message: null,
            PayloadJson: "{}",
            TargetEntityKind: "image",
            TargetEntityId: entityId.ToString(),
            TargetLabel: label,
            CreatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

    private sealed class RecordingImageThumbnailGenerator : IImageThumbnailGenerator {
        public async Task<bool> GenerateAsync(
            string sourcePath,
            string outputPath,
            int maxWidth,
            int jpegQuality,
            CancellationToken cancellationToken) {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, "thumbnail", cancellationToken);
            return true;
        }
    }

    private sealed class RecordingMediaAssetGenerator(string tempDir) : IMediaAssetGenerator {
        public bool GeneratedPreviewClip { get; private set; }
        public int PreviewDurationSeconds { get; private set; }

        public Task<bool> GenerateVideoThumbnailAsync(string inputPath, string outputPath, double seekSeconds, int width, int height, int quality, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<bool> GeneratePreviewClipAsync(string inputPath, string outputPath, double startSeconds, int durationSeconds, CancellationToken cancellationToken) {
            GeneratedPreviewClip = true;
            PreviewDurationSeconds = durationSeconds;
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, "preview", cancellationToken);
            return true;
        }

        public Task<bool> ExtractTrickplayFrameAsync(string inputPath, string outputPath, double seekSeconds, int width, int height, int jpegQuality, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> ExtractTrickplayFramesBatchAsync(string inputPath, string outputDir, double duration, int intervalSeconds, int width, int height, int jpegQuality, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ComposeSpriteSheetAsync(string frameDir, string outputPath, int columns, int frameWidth, int frameHeight, int jpegQuality, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> ComposeTiledJpegSheetsAsync(string frameDir, string outputDir, int columns, int rows, int frameWidth, int frameHeight, int jpegQuality, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<(bool Thumbnail, bool Preview)> GenerateThumbnailAndPreviewAsync(
            string inputPath,
            string thumbnailPath,
            double thumbSeekSeconds,
            int thumbWidth,
            int thumbHeight,
            int thumbQuality,
            string previewPath,
            double previewStartSeconds,
            int previewDurationSeconds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> GenerateImageThumbnailAsync(string inputPath, string outputPath, int targetWidth, int quality, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ExtractSubtitlesAsync(string inputPath, string outputDir, IReadOnlyList<SubtitleStreamData> streams, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int[]?> GenerateWaveformDataAsync(string inputPath, double durationSeconds, int pixelsPerSecond, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public string VideoThumbnailPath(Guid entityId) => throw new NotSupportedException();
        public string VideoPreviewPath(Guid entityId) => throw new NotSupportedException();
        public string VideoSpritePath(Guid entityId) => throw new NotSupportedException();
        public string VideoTrickplayVttPath(Guid entityId) => throw new NotSupportedException();
        public string TrickplayFrameDir(Guid entityId) => throw new NotSupportedException();
        public string TrickplayTileDir(Guid entityId, int width) => throw new NotSupportedException();
        public string ImageThumbnailPath(Guid entityId) => Path.Combine(tempDir, $"{entityId}-thumb.jpg");
        public string ImagePreviewPath(Guid entityId) => Path.Combine(tempDir, $"{entityId}-preview.mp4");
        public string BookPageThumbnailPath(Guid entityId) => throw new NotSupportedException();
        public string BookCoverThumbnailPath(Guid entityId) => throw new NotSupportedException();
        public string AudioWaveformPath(Guid entityId) => throw new NotSupportedException();
        public string SubtitleDir(Guid entityId) => throw new NotSupportedException();
        public string VideoThumbnailUrl(Guid entityId) => throw new NotSupportedException();
        public string VideoPreviewUrl(Guid entityId) => throw new NotSupportedException();
        public string VideoTrickplayVttUrl(Guid entityId) => throw new NotSupportedException();
        public string TrickplayPlaylistUrl(Guid entityId, int width) => throw new NotSupportedException();
        public string ImageThumbnailUrl(Guid entityId) => $"/assets/images/{entityId}/thumb.jpg";
        public string ImagePreviewUrl(Guid entityId) => $"/assets/images/{entityId}/preview.mp4";
        public string BookPageThumbnailUrl(Guid entityId) => throw new NotSupportedException();
        public string BookCoverThumbnailUrl(Guid entityId) => throw new NotSupportedException();
        public string AudioWaveformUrl(Guid entityId) => throw new NotSupportedException();
        public string SubtitleUrl(Guid entityId, string fileName) => throw new NotSupportedException();
    }

    private sealed class ImagePreviewPersistence(string sourcePath) : IMediaProcessingStatePersistence, ILibraryScanRootPersistence {
        public LibrarySettingsData Settings { get; init; } = DefaultSettings;
        public List<(EntityFileRole Role, string Path, string? MimeType)> EntityFiles { get; } = [];

        public Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);
        public Task<string?> GetSourceFilePathAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult<string?>(sourcePath);

        public Task UpsertEntityFileAsync(Guid entityId, EntityFileRole role, string path, string? mimeType, long? sizeBytes, CancellationToken cancellationToken) {
            EntityFiles.Add((role, path, mimeType));
            return Task.CompletedTask;
        }

        public Task UpsertEntityTechnicalAsync(Guid entityId, double? duration, int? width, int? height, double? frameRate, int? bitRate, int? sampleRate, int? channels, string? codec, string? container, string? format, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertMediaSourceAsync(Guid entityId, string path, MediaSourceProbeData source, IReadOnlyList<MediaStreamProbeData> streams, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertTrickplayInfoAsync(Guid entityId, TrickplayInfoData info, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, string value, Guid? entityFileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> GetSourceFileIdAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertSubtitleAsync(Guid entityId, string language, string? label, string format, EntitySubtitleSource source, string storagePath, string sourceFormat, int streamIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertAudioTrackTagsAsync(Guid entityId, string? artist, string? album, int? trackNumber, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntityTechnicalData?> GetEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateRootLastScannedAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetExcludedPathsForRootAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveEntitiesInExcludedPathsAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RemoveOrphanTagsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
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
}
