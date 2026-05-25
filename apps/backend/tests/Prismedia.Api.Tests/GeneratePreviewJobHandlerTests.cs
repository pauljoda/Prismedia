using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Generate;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class GeneratePreviewJobHandlerTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-preview-handler-{Guid.NewGuid():N}");

    public GeneratePreviewJobHandlerTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task TrickplayOnlySettingsDoNotGenerateThumbnailPreviewAssets() {
        var entityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var sourcePath = Path.Combine(_tempDir, "movie.mkv");
        await File.WriteAllTextAsync(sourcePath, "video");
        var assets = new RecordingMediaAssetGenerator(_tempDir);
        var persistence = new PreviewPersistence(sourcePath) {
            Settings = new LibrarySettingsData(
                AutoGenerateMetadata: false,
                AutoGenerateFingerprints: false,
                GeneratePhash: false,
                AutoGeneratePreview: false,
                GenerateTrickplay: true,
                TrickplayIntervalSeconds: 10,
                PreviewClipDurationSeconds: 8,
                ThumbnailQuality: 2,
                TrickplayQuality: 2),
            Technical = new EntityTechnicalData(
                DurationSeconds: 60,
                Width: 1920,
                Height: 1080,
                FrameRate: 24,
                BitRate: null,
                SampleRate: null,
                Channels: null,
                Codec: "hevc",
                Container: "mkv")
        };
        var handler = new GeneratePreviewJobHandler(
            NullLogger<GeneratePreviewJobHandler>.Instance,
            assets,
            persistence);
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.GeneratePreview,
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

        await handler.HandleAsync(new JobContext(job, new NoopJobQueue()), CancellationToken.None);

        Assert.False(assets.GeneratedThumbnailAndPreview);
        Assert.Contains(persistence.EntityFiles, file => file.Role == EntityFileRole.Trickplay);
        Assert.DoesNotContain(persistence.EntityFiles, file => file.Role == EntityFileRole.Thumbnail);
        Assert.DoesNotContain(persistence.EntityFiles, file => file.Role == EntityFileRole.Preview);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class RecordingMediaAssetGenerator(string tempDir) : IMediaAssetGenerator {
        public bool GeneratedThumbnailAndPreview { get; private set; }

        public Task<bool> GenerateVideoThumbnailAsync(string inputPath, string outputPath, double seekSeconds, int width, int height, int quality, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> GeneratePreviewClipAsync(string inputPath, string outputPath, double startSeconds, int durationSeconds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExtractTrickplayFrameAsync(string inputPath, string outputPath, double seekSeconds, int width, int height, int jpegQuality, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> ExtractTrickplayFramesBatchAsync(string inputPath, string outputDir, double duration, int intervalSeconds, int width, int height, int jpegQuality, CancellationToken cancellationToken) {
            Directory.CreateDirectory(outputDir);
            return Task.FromResult(25);
        }

        public Task<bool> ComposeSpriteSheetAsync(string frameDir, string outputPath, int columns, int frameWidth, int frameHeight, int jpegQuality, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<int> ComposeTiledJpegSheetsAsync(string frameDir, string outputDir, int columns, int rows, int frameWidth, int frameHeight, int jpegQuality, CancellationToken cancellationToken) {
            Directory.CreateDirectory(outputDir);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "0.jpg"), "tile", cancellationToken);
            return 1;
        }

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
            CancellationToken cancellationToken) {
            GeneratedThumbnailAndPreview = true;
            Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
            File.WriteAllText(thumbnailPath, "thumb");
            File.WriteAllText(previewPath, "preview");
            return Task.FromResult((true, true));
        }

        public Task<bool> GenerateImageThumbnailAsync(string inputPath, string outputPath, int targetWidth, int quality, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ExtractSubtitlesAsync(string inputPath, string outputDir, IReadOnlyList<SubtitleStreamData> streams, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int[]?> GenerateWaveformDataAsync(string inputPath, double durationSeconds, int pixelsPerSecond, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public string VideoThumbnailPath(Guid entityId) => Path.Combine(tempDir, "thumb.jpg");
        public string VideoPreviewPath(Guid entityId) => Path.Combine(tempDir, "preview.mp4");
        public string VideoSpritePath(Guid entityId) => Path.Combine(tempDir, "sprite.jpg");
        public string VideoTrickplayVttPath(Guid entityId) => Path.Combine(tempDir, "trickplay.vtt");
        public string TrickplayFrameDir(Guid entityId) => Path.Combine(tempDir, "frames");
        public string TrickplayTileDir(Guid entityId, int width) => Path.Combine(tempDir, "trickplay", entityId.ToString(), width.ToString());
        public string ImageThumbnailPath(Guid entityId) => throw new NotSupportedException();
        public string BookPageThumbnailPath(Guid entityId) => throw new NotSupportedException();
        public string AudioWaveformPath(Guid entityId) => throw new NotSupportedException();
        public string SubtitleDir(Guid entityId) => throw new NotSupportedException();
        public string VideoThumbnailUrl(Guid entityId) => $"/assets/videos/{entityId}/thumb.jpg";
        public string VideoPreviewUrl(Guid entityId) => $"/assets/videos/{entityId}/preview.mp4";
        public string VideoTrickplayVttUrl(Guid entityId) => $"/assets/videos/{entityId}/trickplay.vtt";
        public string TrickplayPlaylistUrl(Guid entityId, int width) => $"/Videos/{entityId}/Trickplay/{width}/tiles.m3u8";
        public string ImageThumbnailUrl(Guid entityId) => throw new NotSupportedException();
        public string BookPageThumbnailUrl(Guid entityId) => throw new NotSupportedException();
        public string AudioWaveformUrl(Guid entityId) => throw new NotSupportedException();
        public string SubtitleUrl(Guid entityId, string fileName) => throw new NotSupportedException();
    }

    private sealed class PreviewPersistence(string sourcePath) : ILibraryScanPersistence {
        public LibrarySettingsData Settings { get; init; } = new(
            AutoGenerateMetadata: false,
            AutoGenerateFingerprints: false,
            GeneratePhash: false,
            AutoGeneratePreview: true,
            GenerateTrickplay: true,
            TrickplayIntervalSeconds: 10,
            PreviewClipDurationSeconds: 8,
            ThumbnailQuality: 2,
            TrickplayQuality: 2);
        public EntityTechnicalData? Technical { get; init; }
        public List<(EntityFileRole Role, string Path)> EntityFiles { get; } = [];

        public Task<LibrarySettingsData> GetSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);
        public Task<string?> GetSourceFilePathAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult<string?>(sourcePath);
        public Task<EntityTechnicalData?> GetEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult(Technical);
        public Task UpsertTrickplayInfoAsync(Guid entityId, TrickplayInfoData info, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpsertEntityFileAsync(Guid entityId, EntityFileRole role, string path, string? mimeType, long? sizeBytes, CancellationToken cancellationToken) {
            EntityFiles.Add((role, path));
            return Task.CompletedTask;
        }

        public Task<LibraryRootData?> GetLibraryRootAsync(Guid rootId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryRootData>> GetEnabledRootsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
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
        public Task UpsertEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, string value, Guid? entityFileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> GetSourceFileIdAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertSubtitleAsync(Guid entityId, string language, string? label, string format, EntitySubtitleSource source, string storagePath, string sourceFormat, int streamIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertAudioTrackTagsAsync(Guid entityId, string? artist, string? album, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<EntityRefreshTarget>> GetEntityTreeAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
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
}
