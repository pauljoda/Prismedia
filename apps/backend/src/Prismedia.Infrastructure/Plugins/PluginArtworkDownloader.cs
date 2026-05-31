using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Downloads plugin artwork into the Prismedia cache and updates entity file rows.
/// </summary>
public sealed class PluginArtworkDownloader {
    // A browser-like agent: many image CDNs reject generic/bot agents with 403.
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    private readonly PrismediaDbContext _db;
    private readonly PluginArtworkServiceOptions _options;
    private readonly HttpClient _http;

    public PluginArtworkDownloader(
        PrismediaDbContext db,
        PluginArtworkServiceOptions options,
        HttpClient? http = null) {
        _db = db;
        _options = options;
        _http = http ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any()) {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }
    }

    /// <summary>
    /// Downloads explicitly selected role-to-URL artwork choices for an entity.
    /// </summary>
    public async Task DownloadSelectedImagesAsync(
        Guid entityId,
        IReadOnlyDictionary<string, string?> selectedImages,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        foreach (var (roleCode, url) in selectedImages) {
            if (string.IsNullOrWhiteSpace(url) || !roleCode.TryDecodeAs<EntityFileRole>(out var role)) {
                continue;
            }

            // A single unreachable or blocked image (e.g. a CDN 403) must not fail the whole apply.
            var bytes = await TryDownloadAsync(url, cancellationToken);
            if (bytes is null) {
                continue;
            }

            await UpsertArtworkAsync(entityId, role, roleCode, url, bytes, now, cancellationToken);
        }
    }

    /// <summary>
    /// Downloads one plugin image candidate, swallowing unreachable remote artwork failures.
    /// </summary>
    public async Task DownloadPluginImageAsync(
        EntityRow entity,
        ImageCandidate image,
        EntityFileRole role,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var bytes = await TryDownloadAsync(image.Url, cancellationToken);
        if (bytes is null) {
            return;
        }

        await UpsertArtworkAsync(
            entity.Id,
            role,
            role.ToString().ToLowerInvariant(),
            image.Url,
            bytes,
            now,
            cancellationToken);
        entity.UpdatedAt = now;
    }

    /// <summary>
    /// Fetches remote artwork bytes, returning null when the image is unreachable, blocked, or times
    /// out so callers can skip it without aborting the surrounding metadata apply.
    /// </summary>
    private async Task<byte[]?> TryDownloadAsync(string url, CancellationToken cancellationToken) {
        try {
            return await _http.GetByteArrayAsync(url, cancellationToken);
        } catch (HttpRequestException) {
            return null;
        } catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return null;
        } catch (InvalidOperationException) {
            return null;
        } catch (UriFormatException) {
            return null;
        }
    }

    private async Task UpsertArtworkAsync(
        Guid entityId,
        EntityFileRole role,
        string roleCode,
        string url,
        byte[] bytes,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var ext = ExtensionFromUrl(url);
        var relativePath = Path.Combine("plugins", "artwork", entityId.ToString(), $"{roleCode}-{ShortHash(url)}{ext}");
        var physicalPath = Path.Combine(_options.CacheRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        await File.WriteAllBytesAsync(physicalPath, bytes, cancellationToken);

        var publicPath = $"/assets/{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";
        var existing = await FindEntityFileAsync(entityId, role, cancellationToken);
        if (existing is null) {
            _db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Role = role,
                Path = publicPath,
                MimeType = MimeTypeFromExtension(ext),
                Source = FileSourceKind.Custom.ToCode(),
                CreatedAt = now,
                UpdatedAt = now
            });
        } else {
            existing.Path = publicPath;
            existing.MimeType = MimeTypeFromExtension(ext);
            existing.Source = FileSourceKind.Custom.ToCode();
            existing.UpdatedAt = now;
        }
    }

    private async Task<EntityFileRow?> FindEntityFileAsync(Guid entityId, EntityFileRole role, CancellationToken cancellationToken) =>
        _db.EntityFiles.Local.FirstOrDefault(row => row.EntityId == entityId && row.Role == role)
        ?? await _db.EntityFiles.FirstOrDefaultAsync(row => row.EntityId == entityId && row.Role == role, cancellationToken);

    private static string ShortHash(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();

    private static string ExtensionFromUrl(string url) {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        var ext = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(ext) ? ".jpg" : ext.ToLowerInvariant();
    }

    private static string? MimeTypeFromExtension(string ext) =>
        ext.ToLowerInvariant() switch {
            ".jpg" or ".jpeg" => MediaContentTypes.ImageJpeg,
            ".png" => MediaContentTypes.ImagePng,
            ".webp" => MediaContentTypes.ImageWebp,
            _ => null
        };
}
