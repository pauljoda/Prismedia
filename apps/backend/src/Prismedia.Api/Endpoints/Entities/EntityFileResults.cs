using System.IO.Compression;
using Microsoft.Net.Http.Headers;
using Prismedia.Application.Files;

namespace Prismedia.Api.Endpoints;

internal static class EntityFileResults {
    private static readonly HashSet<string> DirectoryComicImageExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif", ".avif"
    };

    /// <summary>
    /// Entity file URLs are stable across file replacement (same id + role), so give
    /// browsers a bounded freshness window instead of immutable caching; conditional
    /// revalidation via Last-Modified/ETag takes over after it expires. Private because
    /// the endpoint is per-user authorized.
    /// </summary>
    private const string FileCacheControl = "private, max-age=3600";

    internal static async Task<IResult> StreamAsync(
        string path,
        string contentType,
        Func<IResult> notFound,
        CancellationToken cancellationToken,
        string? fileDownloadName = null) {
        if (TrySplitArchiveEntry(path, out var archivePath, out var memberPath)) {
            var archiveStream = await OpenArchiveEntryAsync(archivePath, memberPath, cancellationToken);
            return archiveStream is null
                ? notFound()
                : new CachedResult(
                    Results.File(
                        archiveStream,
                        contentType,
                        fileDownloadName,
                        lastModified: File.GetLastWriteTimeUtc(archivePath),
                        enableRangeProcessing: true),
                    FileCacheControl);
        }

        if (Directory.Exists(path)) {
            var files = EnumerateDirectoryComicFiles(path).ToArray();
            if (files.Length == 0) {
                return notFound();
            }

            var zipPath = await CreateDirectoryZipFileAsync(path, files, cancellationToken);
            return new TemporaryFileResult(zipPath, contentType, fileDownloadName);
        }

        if (!File.Exists(path)) {
            return notFound();
        }

        return new CachedResult(
            Results.File(
                path,
                contentType,
                fileDownloadName,
                enableRangeProcessing: true),
            FileCacheControl);
    }

    /// <summary>Decorates an inner file result with a Cache-Control header.</summary>
    private sealed class CachedResult(IResult inner, string cacheControl) : IResult {
        public Task ExecuteAsync(HttpContext httpContext) {
            httpContext.Response.Headers.CacheControl = cacheControl;
            return inner.ExecuteAsync(httpContext);
        }
    }

    private static IEnumerable<string> EnumerateDirectoryComicFiles(string directory) {
        IEnumerable<string> files;
        try {
            files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
        } catch {
            return [];
        }

        return files
            .Where(file => DirectoryComicImageExtensions.Contains(Path.GetExtension(file)))
            .OrderBy(file => Path.GetRelativePath(directory, file), StringComparer.OrdinalIgnoreCase);
    }

    private static bool TrySplitArchiveEntry(
        string path,
        out string archivePath,
        out string memberPath) =>
        EntitySourcePath.TrySplitArchiveMember(path, out archivePath, out memberPath);

    private static async Task<Stream?> OpenArchiveEntryAsync(
        string archivePath,
        string memberPath,
        CancellationToken cancellationToken) {
        if (!File.Exists(archivePath)) {
            return null;
        }

        await using var archiveFile = File.OpenRead(archivePath);
        using var archive = new ZipArchive(archiveFile, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry(memberPath);
        if (entry is null) {
            return null;
        }

        var output = new MemoryStream();
        await using (var entryStream = entry.Open()) {
            await entryStream.CopyToAsync(output, cancellationToken);
        }

        output.Position = 0;
        return output;
    }

    private static async Task<string> CreateDirectoryZipFileAsync(
        string directory,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken) {
        var tempPath = Path.Combine(Path.GetTempPath(), $"prismedia-opds-{Guid.NewGuid():N}.cbz");
        try {
            await using var output = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true)) {
                foreach (var file in files) {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(ZipEntryName(directory, file), CompressionLevel.NoCompression);
                    await using var input = File.OpenRead(file);
                    await using var entryStream = entry.Open();
                    await input.CopyToAsync(entryStream, cancellationToken);
                }
            }

            return tempPath;
        } catch {
            TryDelete(tempPath);
            throw;
        }
    }

    private sealed class TemporaryFileResult(
        string path,
        string contentType,
        string? fileDownloadName) : IResult {
        public async Task ExecuteAsync(HttpContext httpContext) {
            try {
                httpContext.Response.ContentType = contentType;
                httpContext.Response.ContentLength = new FileInfo(path).Length;
                if (!string.IsNullOrWhiteSpace(fileDownloadName)) {
                    httpContext.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") {
                        FileNameStar = fileDownloadName
                    }.ToString();
                }

                await using var input = File.OpenRead(path);
                await input.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
            } finally {
                TryDelete(path);
            }
        }
    }

    private static string ZipEntryName(string directory, string file) =>
        Path.GetRelativePath(directory, file)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

    private static void TryDelete(string path) {
        try {
            File.Delete(path);
        } catch {
            // Best effort cleanup for temporary OPDS archives.
        }
    }
}
