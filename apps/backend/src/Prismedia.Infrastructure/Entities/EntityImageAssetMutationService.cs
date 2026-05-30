using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Physical storage settings for manually uploaded entity artwork.
/// </summary>
/// <param name="CacheRoot">Cache root served by the API under <c>/assets</c>.</param>
public sealed record EntityImageAssetStorageOptions(string CacheRoot);

/// <summary>
/// EF-backed implementation for user-managed poster, header, and thumbnail artwork.
/// </summary>
public sealed class EntityImageAssetMutationService(
    PrismediaDbContext db,
    EntityImageAssetStorageOptions options,
    IGridThumbnailService gridThumbnails) : IEntityImageAssetMutationService {
    private static readonly EntityFileRole[] ManualImageRoles =
    [
        EntityFileRole.Thumbnail,
        EntityFileRole.Poster,
        EntityFileRole.Backdrop,
        EntityFileRole.Cover,
        EntityFileRole.Logo
    ];

    /// <inheritdoc />
    public async Task<EntityImageAssetMutationResult> UploadAsync(
        Guid entityId,
        string role,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken) {
        if (!TryResolveManualRole(role, out var fileRole)) {
            return EntityImageAssetMutationResult.UnsupportedRole;
        }

        var mimeType = NormalizeImageMimeType(contentType, fileName);
        if (mimeType is null) {
            return EntityImageAssetMutationResult.InvalidFile;
        }

        var entity = await db.Entities.FirstOrDefaultAsync(row => row.Id == entityId && row.DeletedAt == null, cancellationToken);
        if (entity is null) {
            return EntityImageAssetMutationResult.NotFound;
        }

        var extension = ExtensionForMimeType(mimeType);
        var now = DateTimeOffset.UtcNow;
        var roleCode = fileRole.ToCode();
        var relativePath = Path.Combine(
            "custom",
            "artwork",
            entityId.ToString(),
            $"{roleCode}-{now.ToUnixTimeMilliseconds()}{extension}");
        var physicalPath = Path.Combine(options.CacheRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        await using (var output = new FileStream(physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
            await content.CopyToAsync(output, cancellationToken);
        }

        var publicPath = $"/assets/{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";
        var existing = await FindEntityFileAsync(entityId, fileRole, cancellationToken);
        if (existing is null) {
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Role = fileRole,
                Path = publicPath,
                MimeType = mimeType,
                SizeBytes = new FileInfo(physicalPath).Length,
                Source = "custom",
                CreatedAt = now,
                UpdatedAt = now
            });
        } else {
            existing.Path = publicPath;
            existing.MimeType = mimeType;
            existing.SizeBytes = new FileInfo(physicalPath).Length;
            existing.Source = "custom";
            existing.UpdatedAt = now;
        }

        entity.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        // Refresh the small grid variant so it tracks the newly chosen cover.
        await gridThumbnails.EnsureAsync(entityId, cancellationToken);
        return EntityImageAssetMutationResult.Updated;
    }

    /// <inheritdoc />
    public async Task<EntityImageAssetMutationResult> ClearAsync(
        Guid entityId,
        string role,
        CancellationToken cancellationToken) {
        if (!TryResolveManualRole(role, out var fileRole)) {
            return EntityImageAssetMutationResult.UnsupportedRole;
        }

        var entity = await db.Entities.FirstOrDefaultAsync(row => row.Id == entityId && row.DeletedAt == null, cancellationToken);
        if (entity is null) {
            return EntityImageAssetMutationResult.NotFound;
        }

        var existing = await FindEntityFileAsync(entityId, fileRole, cancellationToken);
        if (existing is not null) {
            db.EntityFiles.Remove(existing);
        }

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        // Rebuild the grid variant from whatever cover remains after the removal.
        await gridThumbnails.EnsureAsync(entityId, cancellationToken);
        return EntityImageAssetMutationResult.Updated;
    }

    private async Task<EntityFileRow?> FindEntityFileAsync(
        Guid entityId,
        EntityFileRole role,
        CancellationToken cancellationToken) =>
        db.EntityFiles.Local.FirstOrDefault(row => row.EntityId == entityId && row.Role == role)
        ?? await db.EntityFiles.FirstOrDefaultAsync(row => row.EntityId == entityId && row.Role == role, cancellationToken);

    private static bool TryResolveManualRole(string role, out EntityFileRole fileRole) {
        if (role.Equals("header", StringComparison.OrdinalIgnoreCase)) {
            fileRole = EntityFileRole.Backdrop;
            return true;
        }

        if (!role.TryDecodeAs<EntityFileRole>(out fileRole)) {
            return false;
        }

        return ManualImageRoles.Contains(fileRole);
    }

    private static string? NormalizeImageMimeType(string? contentType, string fileName) {
        var normalized = contentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (normalized is "image/jpeg" or "image/png" or "image/webp" or "image/gif" or "image/avif") {
            return normalized;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".avif" => "image/avif",
            _ => null
        };
    }

    private static string ExtensionForMimeType(string mimeType) =>
        mimeType switch {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/avif" => ".avif",
            _ => ".img"
        };
}
