using System.IO.Compression;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Core image thumbnail resizer. Tries SkiaSharp in-process first (no process spawn,
/// covers jpg/png/webp/gif/bmp — the overwhelming majority of files) and falls back to
/// ffmpeg only for inputs Skia cannot decode (heic/avif/svg, or gallery "image" entries
/// that are actually short video clips). The fallback is automatic: Skia returns false
/// when it cannot read the source, so callers never branch on format.
/// </summary>
public sealed class ImageThumbnailGenerator(
    SkiaImageDownscaler skia,
    ThumbnailService ffmpeg) : IImageThumbnailGenerator {
    /// <inheritdoc />
    public async Task<bool> GenerateAsync(
        string sourcePath, string outputPath, int maxWidth, int jpegQuality, CancellationToken cancellationToken) {
        if (EntitySourcePath.TrySplitArchiveMember(sourcePath, out var archivePath, out var memberPath)) {
            return await GenerateArchiveMemberAsync(
                archivePath,
                memberPath,
                outputPath,
                maxWidth,
                jpegQuality,
                cancellationToken);
        }

        return await GenerateFileAsync(sourcePath, outputPath, maxWidth, jpegQuality, cancellationToken);
    }

    private async Task<bool> GenerateArchiveMemberAsync(
        string archivePath,
        string memberPath,
        string outputPath,
        int maxWidth,
        int jpegQuality,
        CancellationToken cancellationToken) {
        if (!File.Exists(archivePath)) return false;

        var extension = Path.GetExtension(memberPath);
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"prismedia-thumbnail-{Guid.NewGuid():N}{extension}");
        try {
            await using var input = File.OpenRead(archivePath);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
            var entry = archive.GetEntry(memberPath);
            if (entry is null) return false;

            await using (var entryStream = entry.Open())
            await using (var output = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan)) {
                await entryStream.CopyToAsync(output, cancellationToken);
            }

            return await GenerateFileAsync(tempPath, outputPath, maxWidth, jpegQuality, cancellationToken);
        } catch (InvalidDataException) {
            return false;
        } catch (IOException) {
            return false;
        } finally {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private async Task<bool> GenerateFileAsync(
        string sourcePath,
        string outputPath,
        int maxWidth,
        int jpegQuality,
        CancellationToken cancellationToken) {
        if (skia.Downscale(sourcePath, outputPath, maxWidth, jpegQuality)) {
            return true;
        }

        // ffmpeg uses an inverse -q:v scale (lower is better); map the JPEG quality onto its useful range.
        var ffmpegQuality = Math.Clamp((100 - jpegQuality) / 10 + 1, 2, 10);
        return await ffmpeg.GenerateImageThumbnailAsync(sourcePath, outputPath, maxWidth, ffmpegQuality, cancellationToken);
    }
}
