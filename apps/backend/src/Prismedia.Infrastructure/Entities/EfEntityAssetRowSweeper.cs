using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Deletes entity file rows whose <c>/assets/</c> file no longer exists on disk. Runs off the
/// request path so list projections can trust file rows without stat-ing artwork per card. Rows
/// pointing outside the managed cache root are ignored (media-library files have their own scan
/// reconciliation). Batched by primary key so a large library never materializes at once.
/// </summary>
public sealed class EfEntityAssetRowSweeper(
    PrismediaDbContext db,
    AssetPathService assets,
    ILogger<EfEntityAssetRowSweeper> logger) : IEntityAssetRowSweeper {
    private const int BatchSize = 1000;

    /// <inheritdoc />
    public async Task<int> SweepAsync(CancellationToken cancellationToken) {
        var prefix = AssetPaths.AssetsUrlPrefix;
        var removedTotal = 0;
        var lastId = Guid.Empty;
        while (!cancellationToken.IsCancellationRequested) {
            var batch = await db.EntityFiles.AsNoTracking()
                .Where(file => file.Id > lastId && file.Path.StartsWith(prefix))
                .OrderBy(file => file.Id)
                .Take(BatchSize)
                .Select(file => new { file.Id, file.Path })
                .ToArrayAsync(cancellationToken);
            if (batch.Length == 0) {
                break;
            }

            lastId = batch[^1].Id;
            var missing = batch
                .Where(file =>
                    assets.ResolveAssetDiskPath(file.Path) is { } diskPath &&
                    !File.Exists(diskPath))
                .Select(file => file.Id)
                .ToArray();
            if (missing.Length > 0) {
                if (db.Database.IsRelational()) {
                    removedTotal += await db.EntityFiles
                        .Where(file => missing.Contains(file.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                } else {
                    // The in-memory test provider cannot translate ExecuteDelete.
                    var rows = await db.EntityFiles
                        .Where(file => missing.Contains(file.Id))
                        .ToArrayAsync(cancellationToken);
                    db.EntityFiles.RemoveRange(rows);
                    await db.SaveChangesAsync(cancellationToken);
                    removedTotal += rows.Length;
                }
            }
        }

        if (removedTotal > 0) {
            logger.LogWarning(
                "Removed {Count} entity file row(s) whose generated assets were missing on disk.",
                removedTotal);
        }

        return removedTotal;
    }
}
