using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Destructively rolls an interrupted acquisition import back to a fileless state. Every path comes from
/// the validated durable checkpoint; the cleanup never discovers or guesses neighboring library files.
/// </summary>
public sealed class AcquisitionImportResetCleanup(
    PrismediaDbContext db,
    VideoScanConcurrencyGate scanGate,
    ILogger<AcquisitionImportResetCleanup> logger) : IAcquisitionImportResetCleanup {
    /// <inheritdoc />
    public async Task CleanupAsync(
        AcquisitionImportContext import,
        CancellationToken cancellationToken) {
        await using var scanLease = await scanGate.EnterAsync(cancellationToken);

        var catalogChanges = import.ImportPlacementCheckpoint is { } placement
            ? CleanupPlacementFiles(placement, cancellationToken)
            : CleanupTvFiles(import, cancellationToken);

        await ReconcileCatalogAsync(import.EntityId, catalogChanges, cancellationToken);
    }

    private CatalogChanges CleanupPlacementFiles(
        ImportPlacementCheckpoint checkpoint,
        CancellationToken cancellationToken) {
        var removedTargets = new HashSet<string>(FileSystemPathComparison.Comparer);
        foreach (var unit in checkpoint.Units) {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteFileBestEffort(unit.TargetAbsolutePath);
            DeleteFileBestEffort(unit.SourceAbsolutePath);
            removedTargets.Add(Path.GetFullPath(unit.TargetAbsolutePath));
            DeleteEmptyParents(unit.TargetAbsolutePath, checkpoint.LibraryRootPath);
            DeleteEmptyParents(unit.SourceAbsolutePath, checkpoint.PayloadRootPath);
        }

        return new CatalogChanges(
            removedTargets,
            new Dictionary<string, string>(FileSystemPathComparison.Comparer));
    }

    private CatalogChanges CleanupTvFiles(
        AcquisitionImportContext import,
        CancellationToken cancellationToken) {
        if (import.TvImportCheckpoint is not { } checkpoint) {
            return new CatalogChanges(
                new HashSet<string>(FileSystemPathComparison.Comparer),
                new Dictionary<string, string>(FileSystemPathComparison.Comparer));
        }

        var removedTargets = new HashSet<string>(FileSystemPathComparison.Comparer);
        var restoredTargets = new Dictionary<string, string>(FileSystemPathComparison.Comparer);
        foreach (var unit in checkpoint.Units) {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(unit.TargetAbsolutePath);
            // An adopted target predates the acquisition, so reset may clean the downloaded duplicate
            // and catalog state but must never delete the pre-existing library bytes.
            if (!unit.AdoptedExistingTarget) {
                if (unit.PreviousFilePath is { } previousPath) {
                    RestorePreviousFile(unit, target, Path.GetFullPath(previousPath));
                    if (!FileSystemPathComparison.Equals(target, previousPath)) {
                        removedTargets.Add(target);
                        restoredTargets[target] = Path.GetFullPath(previousPath);
                    }
                } else {
                    DeleteFileBestEffort(target);
                    removedTargets.Add(target);
                }
            }

            DeleteFileBestEffort(unit.SourceAbsolutePath);
            DeleteFileBestEffort(OwnedFileReplacementArtifacts.StagedPath(
                Path.GetFullPath(unit.PreviousFilePath ?? target)));
            DeleteFileBestEffort(unit.ReplacementEvidencePath);
            DeleteFileBestEffort(unit.ReplacementBackupPath);
            DeleteEmptyParents(target, checkpoint.SeriesFolderPath);
        }

        return new CatalogChanges(removedTargets, restoredTargets);
    }

    private void RestorePreviousFile(TvImportCheckpointUnit unit, string target, string previous) {
        var backup = unit.ReplacementBackupPath;
        if (!string.IsNullOrWhiteSpace(backup) && File.Exists(backup)) {
            TryFileOperation(
                () => {
                    Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
                    File.Copy(backup, previous, overwrite: true);
                },
                backup,
                "restore the pre-import file");
        }

        if (!FileSystemPathComparison.Equals(target, previous)) {
            DeleteFileBestEffort(target);
        }
    }

    private async Task ReconcileCatalogAsync(
        Guid? entityId,
        CatalogChanges changes,
        CancellationToken cancellationToken) {
        if (changes.RemovedTargets.Count == 0 && changes.RestoredTargets.Count == 0) {
            return;
        }

        var targetLengths = changes.RemovedTargets.Select(path => path.Length).Distinct().ToArray();
        var sourceRows = await db.EntityFiles
            .Where(file => file.Role == EntityFileRole.Source && targetLengths.Contains(file.Path.Length))
            .ToArrayAsync(cancellationToken);
        foreach (var row in sourceRows) {
            var target = changes.RemovedTargets.FirstOrDefault(path =>
                FileSystemPathComparison.Equals(path, row.Path));
            if (target is null) {
                continue;
            }

            if (changes.RestoredTargets.TryGetValue(target, out var restored)) {
                row.Path = restored;
                row.SizeBytes = File.Exists(restored) ? new FileInfo(restored).Length : null;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            } else {
                db.EntityFiles.Remove(row);
            }
        }

        var snapshotRows = await db.ScannedFiles
            .Where(file => targetLengths.Contains(file.Path.Length))
            .ToArrayAsync(cancellationToken);
        db.ScannedFiles.RemoveRange(snapshotRows.Where(row =>
            changes.RemovedTargets.Contains(row.Path)));

        if (entityId is { } id) {
            var entity = await db.Entities.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
            if (entity is not null) {
                var retainedSource = sourceRows.Any(row =>
                    row.EntityId == id && db.Entry(row).State != EntityState.Deleted);
                if (!retainedSource) {
                    retainedSource = await db.EntityFiles.AnyAsync(row =>
                        row.EntityId == id
                        && row.Role == EntityFileRole.Source
                        && !targetLengths.Contains(row.Path.Length),
                        cancellationToken);
                }

                if (!retainedSource) {
                    entity.IsWanted = true;
                    entity.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private void DeleteFileBestEffort(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }

        TryFileOperation(
            () => {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            },
            path,
            "delete an interrupted import artifact");
    }

    private void DeleteEmptyParents(string path, string boundary) {
        var root = Path.GetFullPath(boundary);
        var current = Path.GetDirectoryName(Path.GetFullPath(path));
        while (current is not null
            && !FileSystemPathComparison.Equals(current, root)
            && FileSystemPathComparison.IsSameOrDescendant(root, current)) {
            try {
                if (Directory.EnumerateFileSystemEntries(current).Any()) {
                    return;
                }
                Directory.Delete(current);
            } catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException) {
                logger.LogDebug(exception, "Could not remove empty acquisition import directory {Path}.", current);
                return;
            }
            current = Path.GetDirectoryName(current);
        }
    }

    private void TryFileOperation(Action operation, string path, string description) {
        try {
            operation();
        } catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
            logger.LogWarning(exception, "Could not {Description} at {Path}; reset will continue.", description, path);
        }
    }

    private sealed record CatalogChanges(
        IReadOnlySet<string> RemovedTargets,
        IReadOnlyDictionary<string, string> RestoredTargets);
}
