using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Settings;

namespace Prismedia.Infrastructure.Media.Adapters;

/// <summary>
/// Adapts the Infrastructure ThumbnailService and AssetPathService to the Application port interface.
/// </summary>
public sealed class MediaAssetGeneratorAdapter(
    ThumbnailService thumbnails,
    AssetPathService paths,
    SettingsService? settings = null,
    MediaToolOptions? defaultToolOptions = null) : IMediaAssetGenerator {
    public async Task<bool> GenerateVideoThumbnailAsync(
        string inputPath, string outputPath, double seekSeconds,
        int width, int height, int quality, CancellationToken cancellationToken) =>
        await thumbnails.GenerateVideoThumbnailAsync(
            inputPath, outputPath, seekSeconds, width, height, quality, cancellationToken,
            await ResolveVideoToolsAsync(cancellationToken));

    public async Task<bool> GeneratePreviewClipAsync(
        string inputPath, string outputPath, double startSeconds, int durationSeconds, CancellationToken cancellationToken) =>
        await thumbnails.GeneratePreviewClipAsync(
            inputPath, outputPath, startSeconds, durationSeconds, cancellationToken,
            await ResolveVideoToolsAsync(cancellationToken));

    public async Task<bool> ExtractTrickplayFrameAsync(
        string inputPath, string outputPath, double seekSeconds,
        int width, int height, int jpegQuality, CancellationToken cancellationToken) =>
        await thumbnails.ExtractTrickplayFrameAsync(
            inputPath, outputPath, seekSeconds, width, height, jpegQuality, cancellationToken,
            await ResolveVideoToolsAsync(cancellationToken));

    public async Task<int> ExtractTrickplayFramesBatchAsync(
        string inputPath, string outputDir, double duration,
        int intervalSeconds, int width, int height, int jpegQuality,
        CancellationToken cancellationToken) =>
        await thumbnails.ExtractTrickplayFramesBatchAsync(
            inputPath, outputDir, duration, intervalSeconds, width, height, jpegQuality, cancellationToken,
            await ResolveVideoToolsAsync(cancellationToken));

    public async Task<bool> ComposeSpriteSheetAsync(
        string frameDir, string outputPath, int columns,
        int frameWidth, int frameHeight, int jpegQuality,
        CancellationToken cancellationToken) =>
        await thumbnails.ComposeSpriteSheetAsync(
            frameDir, outputPath, columns, frameWidth, frameHeight, jpegQuality, cancellationToken,
            await ResolveVideoToolsAsync(cancellationToken));

    public async Task<int> ComposeTiledJpegSheetsAsync(
        string frameDir,
        string outputDir,
        int columns,
        int rows,
        int frameWidth,
        int frameHeight,
        int jpegQuality,
        CancellationToken cancellationToken) =>
        await thumbnails.ComposeTiledJpegSheetsAsync(
            frameDir, outputDir, columns, rows, frameWidth, frameHeight, jpegQuality, cancellationToken,
            await ResolveVideoToolsAsync(cancellationToken));

    public async Task<(bool Thumbnail, bool Preview)> GenerateThumbnailAndPreviewAsync(
        string inputPath,
        string thumbnailPath, double thumbSeekSeconds, int thumbWidth, int thumbHeight, int thumbQuality,
        string previewPath, double previewStartSeconds, int previewDurationSeconds,
        CancellationToken cancellationToken) =>
        await thumbnails.GenerateThumbnailAndPreviewAsync(inputPath, thumbnailPath, thumbSeekSeconds, thumbWidth, thumbHeight, thumbQuality,
            previewPath, previewStartSeconds, previewDurationSeconds, cancellationToken,
            await ResolveVideoToolsAsync(cancellationToken));

    public async Task<bool> GenerateImageThumbnailAsync(
        string inputPath, string outputPath, int targetWidth, int quality, CancellationToken cancellationToken) =>
        await thumbnails.GenerateImageThumbnailAsync(
            inputPath, outputPath, targetWidth, quality, cancellationToken,
            await ResolveVideoToolsAsync(cancellationToken));

    public async Task<IReadOnlyList<string>> ExtractSubtitlesAsync(
        string inputPath, string outputDir, IReadOnlyList<SubtitleStreamData> streams, CancellationToken cancellationToken) {
        var infraStreams = streams
            .Select(s => new SubtitleStreamInfo(s.StreamIndex, s.CodecName, s.Language, s.Title))
            .ToList();
        return await thumbnails.ExtractSubtitlesAsync(
            inputPath, outputDir, infraStreams, cancellationToken,
            await ResolveVideoToolsAsync(cancellationToken));
    }

    public async Task<int[]?> GenerateWaveformDataAsync(
        string inputPath, double durationSeconds, int pixelsPerSecond, CancellationToken cancellationToken) =>
        await thumbnails.GenerateWaveformDataAsync(
            inputPath, durationSeconds, pixelsPerSecond, cancellationToken,
            await ResolveVideoToolsAsync(cancellationToken));

    public string VideoThumbnailPath(Guid entityId) => paths.VideoThumbnailPath(entityId);
    public string VideoPreviewPath(Guid entityId) => paths.VideoPreviewPath(entityId);
    public string VideoSpritePath(Guid entityId) => paths.VideoSpritePath(entityId);
    public string VideoTrickplayVttPath(Guid entityId) => paths.VideoTrickplayVttPath(entityId);
    public string TrickplayFrameDir(Guid entityId) => paths.TrickplayFrameDir(entityId);
    public string TrickplayTileDir(Guid entityId, int width) => paths.TrickplayTileDir(entityId, width);
    public string ImageThumbnailPath(Guid entityId) => paths.ImageThumbnailPath(entityId);
    public string BookPageThumbnailPath(Guid entityId) => paths.BookPageThumbnailPath(entityId);
    public string BookCoverThumbnailPath(Guid entityId) => paths.BookCoverThumbnailPath(entityId);
    public string AudioWaveformPath(Guid entityId) => paths.AudioWaveformPath(entityId);
    public string SubtitleDir(Guid entityId) => paths.SubtitleDir(entityId);

    public string VideoThumbnailUrl(Guid entityId) => AssetPathService.VideoThumbnailUrl(entityId);
    public string VideoPreviewUrl(Guid entityId) => AssetPathService.VideoPreviewUrl(entityId);
    public string VideoTrickplayVttUrl(Guid entityId) => AssetPathService.VideoTrickplayVttUrl(entityId);
    public string TrickplayPlaylistUrl(Guid entityId, int width) => AssetPathService.TrickplayPlaylistUrl(entityId, width);
    public string ImageThumbnailUrl(Guid entityId) => AssetPathService.ImageThumbnailUrl(entityId);
    public string BookPageThumbnailUrl(Guid entityId) => AssetPathService.BookPageThumbnailUrl(entityId);
    public string BookCoverThumbnailUrl(Guid entityId) => AssetPathService.BookCoverThumbnailUrl(entityId);
    public string AudioWaveformUrl(Guid entityId) => AssetPathService.AudioWaveformUrl(entityId);
    public string SubtitleUrl(Guid entityId, string fileName) => AssetPathService.SubtitleUrl(entityId, fileName);

    private async Task<MediaToolOptions> ResolveVideoToolsAsync(CancellationToken cancellationToken) {
        var defaults = defaultToolOptions ?? new MediaToolOptions();
        if (settings is null) {
            return defaults;
        }

        var hlsSettings = await settings.GetHlsSettingsAsync(cancellationToken);
        var ffmpegPath = string.IsNullOrWhiteSpace(hlsSettings.FfmpegPath) ||
            string.Equals(hlsSettings.FfmpegPath.Trim(), "ffmpeg", StringComparison.OrdinalIgnoreCase)
                ? defaults.FfmpegPath
                : hlsSettings.FfmpegPath.Trim();
        return new MediaToolOptions(ffmpegPath, defaults.ConfiguredFfprobePath);
    }
}
