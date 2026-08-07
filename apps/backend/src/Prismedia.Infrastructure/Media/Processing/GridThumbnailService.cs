using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Generates small grid-card cover variants for artwork kinds that benefit from raster
/// downscaling, then records them as
/// <see cref="EntityFileRole.GridThumbnail"/> (480w) and
/// <see cref="EntityFileRole.GridThumbnail2x"/> (960w) files. Presentation kinds whose
/// original format must be preserved, such as scalable Studio logos, bypass this service.
/// </summary>
public sealed class GridThumbnailService(
    PrismediaDbContext db,
    AssetPathService assets,
    IImageThumbnailGenerator imageThumbnails) : IGridThumbnailService {
    /// <summary>Target width for grid cards at standard density.</summary>
    private const int GridThumbnailMaxWidth = 480;

    /// <summary>Target width for grid cards on high-DPI displays (2x the standard variant).</summary>
    private const int GridThumbnail2xMaxWidth = 960;

    private const int GridThumbnailJpegQuality = 80;

    /// <inheritdoc />
    public async Task EnsureAsync(Guid entityId, CancellationToken cancellationToken) {
        var kindCode = await db.Entities
            .AsNoTracking()
            .Where(entity => entity.Id == entityId)
            .Select(entity => entity.KindCode)
            .SingleOrDefaultAsync(cancellationToken);
        if (PreservesOriginalArtwork(kindCode)) {
            return;
        }

        var coverFiles = await db.EntityFiles
            .AsNoTracking()
            .Where(file => file.EntityId == entityId && EntityCoverSelection.CoverRoles.Contains(file.Role))
            .ToListAsync(cancellationToken);

        var cover = EntityCoverSelection.Select(coverFiles);
        if (cover is null) {
            return;
        }

        var sourcePath = assets.ResolveAssetDiskPath(cover.Path);
        if (sourcePath is null || !File.Exists(sourcePath)) {
            return;
        }

        var generatedStandard = await GenerateVariantAsync(
            entityId,
            EntityFileRole.GridThumbnail,
            sourcePath,
            assets.GridThumbnailPath(entityId),
            AssetPathService.GridThumbnailUrl(entityId),
            GridThumbnailMaxWidth,
            cancellationToken);
        if (!generatedStandard) {
            return;
        }

        await GenerateVariantAsync(
            entityId,
            EntityFileRole.GridThumbnail2x,
            sourcePath,
            assets.GridThumbnail2xPath(entityId),
            AssetPathService.GridThumbnail2xUrl(entityId),
            GridThumbnail2xMaxWidth,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListEntitiesNeedingRefreshAsync(CancellationToken cancellationToken) {
        var coverFiles = await db.EntityFiles
            .AsNoTracking()
            .Where(file => EntityCoverSelection.CoverRoles.Contains(file.Role))
            .ToListAsync(cancellationToken);
        var coverEntityIds = coverFiles.Select(file => file.EntityId).Distinct().ToArray();
        var kindsByEntity = await db.Entities
            .AsNoTracking()
            .Where(entity => coverEntityIds.Contains(entity.Id))
            .ToDictionaryAsync(entity => entity.Id, entity => entity.KindCode, cancellationToken);

        var variantsByEntity = (await db.EntityFiles
                .AsNoTracking()
                .Where(file => file.Role == EntityFileRole.GridThumbnail || file.Role == EntityFileRole.GridThumbnail2x)
                .ToListAsync(cancellationToken))
            .ToLookup(file => file.EntityId);

        var needed = new List<Guid>();
        foreach (var group in coverFiles.GroupBy(file => file.EntityId)) {
            if (PreservesOriginalArtwork(kindsByEntity.GetValueOrDefault(group.Key))) {
                continue;
            }

            var cover = EntityCoverSelection.Select(group);
            if (cover is null) {
                continue;
            }

            var sourcePath = assets.ResolveAssetDiskPath(cover.Path);
            if (sourcePath is null || !File.Exists(sourcePath)) {
                continue;
            }

            var variants = variantsByEntity[group.Key].ToArray();
            var standard = variants.FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail);
            var large = variants.FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail2x);
            if (standard is null || large is null ||
                standard.UpdatedAt < cover.UpdatedAt || large.UpdatedAt < cover.UpdatedAt ||
                MissingOnDisk(standard) || MissingOnDisk(large)) {
                needed.Add(group.Key);
            }
        }

        return needed;
    }

    private bool MissingOnDisk(EntityFileRow variant) {
        var path = assets.ResolveAssetDiskPath(variant.Path);
        return path is null || !File.Exists(path);
    }

    private async Task<bool> GenerateVariantAsync(
        Guid entityId,
        EntityFileRole role,
        string sourcePath,
        string outputPath,
        string url,
        int maxWidth,
        CancellationToken cancellationToken) {
        if (!await imageThumbnails.GenerateAsync(
                sourcePath,
                outputPath,
                maxWidth,
                GridThumbnailJpegQuality,
                cancellationToken)) {
            return false;
        }

        var size = new FileInfo(outputPath).Length;
        var now = DateTimeOffset.UtcNow;
        var existing = await db.EntityFiles
            .FirstOrDefaultAsync(
                file => file.EntityId == entityId && file.Role == role,
                cancellationToken);

        if (existing is null) {
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Role = role,
                Path = url,
                MimeType = MediaContentTypes.ImageJpeg,
                SizeBytes = size,
                Source = FileSourceKind.Scan.ToCode(),
                CreatedAt = now,
                UpdatedAt = now
            });
        } else {
            existing.Path = url;
            existing.MimeType = MediaContentTypes.ImageJpeg;
            existing.SizeBytes = size;
            existing.UpdatedAt = now;
        }

        return true;
    }

    private static bool PreservesOriginalArtwork(string? kindCode) =>
        EntityKindRegistry.TryDescribe(kindCode, out var definition) &&
        definition.Presentation.ArtworkSurface == EntityArtworkSurface.BrandPlate;
}
