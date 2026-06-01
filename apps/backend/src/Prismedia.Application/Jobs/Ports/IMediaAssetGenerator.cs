namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for generating thumbnails, previews, sprites, waveforms, and extracting subtitles.
/// </summary>
public interface IMediaAssetGenerator {
    Task<bool> GenerateVideoThumbnailAsync(
        string inputPath, string outputPath, double seekSeconds,
        int width, int height, int quality, CancellationToken cancellationToken);

    Task<bool> GeneratePreviewClipAsync(
        string inputPath, string outputPath,
        double startSeconds, int durationSeconds, CancellationToken cancellationToken);

    Task<bool> ExtractTrickplayFrameAsync(
        string inputPath, string outputPath, double seekSeconds,
        int width, int height, int jpegQuality, CancellationToken cancellationToken);

    /// <summary>
    /// Extracts all trickplay frames in a single ffmpeg pass using the fps filter.
    /// Returns the number of frames actually extracted.
    /// </summary>
    Task<int> ExtractTrickplayFramesBatchAsync(
        string inputPath, string outputDir, double duration,
        int intervalSeconds, int width, int height, int jpegQuality,
        CancellationToken cancellationToken);

    /// <summary>
    /// Composites individual trickplay frame images into a single sprite-sheet JPEG
    /// using a tile layout with the given column count. Returns true on success.
    /// </summary>
    Task<bool> ComposeSpriteSheetAsync(
        string frameDir, string outputPath, int columns,
        int frameWidth, int frameHeight, int jpegQuality,
        CancellationToken cancellationToken);

    Task<int> ComposeTiledJpegSheetsAsync(
        string frameDir,
        string outputDir,
        int columns,
        int rows,
        int frameWidth,
        int frameHeight,
        int jpegQuality,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generates both thumbnail and preview clip, sharing decode overhead where possible.
    /// Returns success flags for each output.
    /// </summary>
    Task<(bool Thumbnail, bool Preview)> GenerateThumbnailAndPreviewAsync(
        string inputPath,
        string thumbnailPath, double thumbSeekSeconds, int thumbWidth, int thumbHeight, int thumbQuality,
        string previewPath, double previewStartSeconds, int previewDurationSeconds,
        CancellationToken cancellationToken);

    Task<bool> GenerateImageThumbnailAsync(
        string inputPath, string outputPath,
        int targetWidth, int quality, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ExtractSubtitlesAsync(
        string inputPath, string outputDir,
        IReadOnlyList<SubtitleStreamData> streams, CancellationToken cancellationToken);

    Task<int[]?> GenerateWaveformDataAsync(
        string inputPath, double durationSeconds, int pixelsPerSecond, CancellationToken cancellationToken);

    string VideoThumbnailPath(Guid entityId);
    string VideoPreviewPath(Guid entityId);
    string VideoSpritePath(Guid entityId);
    string VideoTrickplayVttPath(Guid entityId);
    string TrickplayFrameDir(Guid entityId);
    string TrickplayTileDir(Guid entityId, int width);
    string ImageThumbnailPath(Guid entityId);
    string BookPageThumbnailPath(Guid entityId);
    string BookCoverThumbnailPath(Guid entityId);
    string AudioWaveformPath(Guid entityId);
    string SubtitleDir(Guid entityId);

    string VideoThumbnailUrl(Guid entityId);
    string VideoPreviewUrl(Guid entityId);
    string VideoTrickplayVttUrl(Guid entityId);
    string TrickplayPlaylistUrl(Guid entityId, int width);
    string ImageThumbnailUrl(Guid entityId);
    string BookPageThumbnailUrl(Guid entityId);
    string BookCoverThumbnailUrl(Guid entityId);
    string AudioWaveformUrl(Guid entityId);
    string SubtitleUrl(Guid entityId, string fileName);
}
