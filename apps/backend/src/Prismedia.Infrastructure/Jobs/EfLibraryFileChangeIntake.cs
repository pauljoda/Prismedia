using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Jobs;

/// <summary>PostgreSQL-backed coalescing ledger for exact filesystem-change observations.</summary>
public sealed class EfLibraryFileChangeIntake(PrismediaDbContext db) : ILibraryFileChangeIntake {
    /// <inheritdoc />
    public async Task RecordAsync(
        Guid rootId,
        string scanKind,
        IReadOnlyCollection<string> absolutePaths,
        CancellationToken cancellationToken) {
        var paths = absolutePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(FileSystemPathComparison.Comparer)
            .ToArray();
        if (paths.Length == 0) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational()) {
            foreach (var path in paths) {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO library_file_change_intents (library_root_id, scan_kind, path, observed_at)
                    VALUES ({rootId}, {scanKind}, {path}, {now})
                    ON CONFLICT (library_root_id, scan_kind, path)
                    DO UPDATE SET observed_at = EXCLUDED.observed_at
                    """, cancellationToken);
            }
            return;
        }

        var existing = await db.LibraryFileChangeIntents
            .Where(row => row.LibraryRootId == rootId
                && row.ScanKind == scanKind
                && paths.Contains(row.Path))
            .ToListAsync(cancellationToken);
        var existingByPath = existing.ToDictionary(row => row.Path, FileSystemPathComparison.Comparer);
        foreach (var path in paths) {
            if (existingByPath.TryGetValue(path, out var row)) {
                row.ObservedAt = now;
            } else {
                db.LibraryFileChangeIntents.Add(new LibraryFileChangeIntentRow {
                    LibraryRootId = rootId,
                    ScanKind = scanKind,
                    Path = path,
                    ObservedAt = now
                });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LibraryFileChangeBatch> LoadAsync(
        Guid rootId,
        string scanKind,
        int limit,
        CancellationToken cancellationToken) {
        if (limit <= 0) {
            return LibraryFileChangeBatch.Empty;
        }
        var rows = await db.LibraryFileChangeIntents.AsNoTracking()
            .Where(row => row.LibraryRootId == rootId && row.ScanKind == scanKind)
            .OrderBy(row => row.ObservedAt)
            .ThenBy(row => row.Path)
            .Take(limit)
            .Select(row => new { row.Path, row.ObservedAt })
            .ToArrayAsync(cancellationToken);
        return rows.Length == 0
            ? LibraryFileChangeBatch.Empty
            : new LibraryFileChangeBatch(
                rows.Select(row => row.Path).ToArray(),
                rows.Max(row => row.ObservedAt));
    }

    /// <inheritdoc />
    public Task<bool> HasPendingAsync(
        Guid rootId,
        string scanKind,
        CancellationToken cancellationToken) =>
        db.LibraryFileChangeIntents.AsNoTracking().AnyAsync(
            row => row.LibraryRootId == rootId && row.ScanKind == scanKind,
            cancellationToken);

    /// <inheritdoc />
    public async Task CompleteAsync(
        Guid rootId,
        string scanKind,
        IReadOnlyCollection<string> absolutePaths,
        DateTimeOffset observedThrough,
        CancellationToken cancellationToken) {
        if (absolutePaths.Count == 0) {
            return;
        }
        var paths = absolutePaths.ToArray();
        var query = db.LibraryFileChangeIntents.Where(row =>
            row.LibraryRootId == rootId
            && row.ScanKind == scanKind
            && paths.Contains(row.Path)
            && row.ObservedAt <= observedThrough);
        if (db.Database.IsRelational()) {
            await query.ExecuteDeleteAsync(cancellationToken);
            return;
        }
        db.LibraryFileChangeIntents.RemoveRange(await query.ToArrayAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
    }
}
