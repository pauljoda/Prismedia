using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Jobs;

/// <summary>
/// EF Core adapter for <see cref="IScanSnapshotStore"/>. Stores one <see cref="ScannedFileRow"/> per
/// file a scan last saw, keyed by <c>(root, scan kind, path)</c>, and applies a computed delta as the
/// minimal set of inserts, updates, and deletes so a rescan only writes what changed.
/// </summary>
public sealed class EfScanSnapshotStore(PrismediaDbContext db) : IScanSnapshotStore {
    /// <inheritdoc />
    public async Task<IReadOnlyList<FileSignature>> LoadAsync(
        Guid rootId, string scanKind, CancellationToken cancellationToken) =>
        await db.ScannedFiles.AsNoTracking()
            .Where(row => row.LibraryRootId == rootId && row.ScanKind == scanKind)
            .Select(row => new FileSignature(row.Path, row.SizeBytes, row.ModifiedTicks))
            .ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task ApplyAsync(
        Guid rootId, string scanKind, ScanDelta delta, CancellationToken cancellationToken) {
        if (!delta.HasChanges) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var existingRows = delta.Changed.Count > 0 || delta.Removed.Count > 0
            ? await db.ScannedFiles
                .Where(row => row.LibraryRootId == rootId && row.ScanKind == scanKind)
                .ToArrayAsync(cancellationToken)
            : [];

        foreach (var added in delta.Added) {
            db.ScannedFiles.Add(new ScannedFileRow {
                LibraryRootId = rootId,
                ScanKind = scanKind,
                Path = added.Path,
                SizeBytes = added.SizeBytes,
                ModifiedTicks = added.ModifiedTicks,
                UpdatedAt = now
            });
        }

        if (delta.Changed.Count > 0) {
            var changedByPath = delta.Changed.ToDictionary(
                signature => signature.Path, FileSystemPathComparison.Comparer);
            foreach (var row in existingRows) {
                if (!changedByPath.TryGetValue(row.Path, out var signature)) {
                    continue;
                }

                row.SizeBytes = signature.SizeBytes;
                row.ModifiedTicks = signature.ModifiedTicks;
                row.UpdatedAt = now;
            }
        }

        if (delta.Removed.Count > 0) {
            var removedPaths = delta.Removed
                .Select(signature => signature.Path)
                .ToHashSet(FileSystemPathComparison.Comparer);
            var toRemove = existingRows
                .Where(row => removedPaths.Contains(row.Path))
                .ToArray();
            db.ScannedFiles.RemoveRange(toRemove);
        }

        // Inserts, updates, and deletes apply in a single transaction so a partially-saved snapshot
        // never disagrees with what the scan just processed.
        await db.SaveChangesAsync(cancellationToken);
    }
}
