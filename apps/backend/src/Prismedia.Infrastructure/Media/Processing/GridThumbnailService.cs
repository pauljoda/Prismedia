using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Generates the small grid-card cover variant for an entity by downscaling its
/// currently resolved cover with SkiaSharp, then records it as a
/// <see cref="EntityFileRole.GridThumbnail"/> file. The variant always derives from
/// the same cover the read projection picks, so the full and small images stay a
/// consistent pair in the frontend <c>srcset</c>.
/// </summary>
public sealed class GridThumbnailService(
    PrismediaDbContext db,
    AssetPathService assets,
    IImageThumbnailGenerator imageThumbnails) : IGridThumbnailService {
    /// <summary>Target width for grid cards. Comfortably covers small/medium cards at 2x DPR.</summary>
    private const int GridThumbnailMaxWidth = 480;
    private const int GridThumbnailJpegQuality = 80;

    /// <inheritdoc />
    public async Task EnsureAsync(Guid entityId, CancellationToken cancellationToken) {
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

        var outputPath = assets.GridThumbnailPath(entityId);
        if (!await imageThumbnails.GenerateAsync(sourcePath, outputPath, GridThumbnailMaxWidth, GridThumbnailJpegQuality, cancellationToken)) {
            return;
        }

        var url = AssetPathService.GridThumbnailUrl(entityId);
        var size = new FileInfo(outputPath).Length;
        var now = DateTimeOffset.UtcNow;
        var existing = await db.EntityFiles
            .FirstOrDefaultAsync(
                file => file.EntityId == entityId && file.Role == EntityFileRole.GridThumbnail,
                cancellationToken);

        if (existing is null) {
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Role = EntityFileRole.GridThumbnail,
                Path = url,
                MimeType = "image/jpeg",
                SizeBytes = size,
                Source = "scan",
                CreatedAt = now,
                UpdatedAt = now
            });
        } else {
            existing.Path = url;
            existing.MimeType = "image/jpeg";
            existing.SizeBytes = size;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
