using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Contracts.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Files;

/// <summary>
/// EF Core adapter for Files page root metadata, linked-entity lookup, and source-path
/// rewrite operations after filesystem moves.
/// </summary>
public sealed class EfFilesPersistence(PrismediaDbContext db) : IFilesPersistence {
    /// <inheritdoc />
    public async Task<IReadOnlyList<FileLibraryRoot>> ListRootsAsync(CancellationToken cancellationToken) =>
        await db.LibraryRoots.AsNoTracking()
            .OrderBy(root => root.Label)
            .Select(root => new FileLibraryRoot(
                root.Id,
                root.Path,
                root.Label,
                root.Enabled,
                root.ScanVideos,
                root.ScanImages,
                root.ScanAudio,
                root.ScanBooks,
                root.IsNsfw))
            .ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<FileLibraryRoot?> GetRootAsync(Guid rootId, CancellationToken cancellationToken) =>
        await db.LibraryRoots.AsNoTracking()
            .Where(root => root.Id == rootId)
            .Select(root => new FileLibraryRoot(
                root.Id,
                root.Path,
                root.Label,
                root.Enabled,
                root.ScanVideos,
                root.ScanImages,
                root.ScanAudio,
                root.ScanBooks,
                root.IsNsfw))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileLinkedEntity>> ListLinkedEntitiesAsync(
        string absolutePath,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var normalized = Path.GetFullPath(absolutePath);
        // Database collations cannot express the media host's path semantics (notably PostgreSQL on a
        // Windows mount). Project the narrow source identity columns and decide containment in memory.
        var sourceCandidates = await db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source)
            .Select(file => new { file.EntityId, file.Path })
            .ToArrayAsync(cancellationToken);
        var linkedIds = sourceCandidates
            .Where(file => FileSystemPathComparison.IsSameOrDescendant(normalized, file.Path))
            .Select(file => file.EntityId)
            .Distinct()
            .ToArray();
        var entityQuery = db.Entities.AsNoTracking()
            .Where(entity => linkedIds.Contains(entity.Id))
            .Select(entity => new { entity.Id, entity.KindCode, entity.Title, entity.IsNsfw });
        if (hideNsfw) {
            entityQuery = entityQuery.Where(entity => !entity.IsNsfw);
        }

        var entities = await entityQuery
            .ToArrayAsync(cancellationToken);

        if (entities.Length == 0) return [];

        var ids = entities.Select(e => e.Id).ToArray();
        var coverByEntity = await db.EntityFiles.AsNoTracking()
            .Where(file => ids.Contains(file.EntityId))
            .Where(file => file.Role == EntityFileRole.Thumbnail || file.Role == EntityFileRole.Poster ||
                           file.Role == EntityFileRole.Cover || file.Role == EntityFileRole.Backdrop)
            .OrderBy(file => file.Role == EntityFileRole.Thumbnail ? 0 :
                file.Role == EntityFileRole.Poster ? 1 :
                file.Role == EntityFileRole.Cover ? 2 : 3)
            .ThenBy(file => file.CreatedAt)
            .GroupBy(file => file.EntityId)
            .Select(group => new { group.Key, Path = group.First().Path })
            .ToDictionaryAsync(x => x.Key, x => x.Path, cancellationToken);

        return entities.Select(entity => new FileLinkedEntity(
            entity.Id,
            entity.KindCode.DecodeAs<EntityKind>(),
            entity.Title,
            coverByEntity.GetValueOrDefault(entity.Id))).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> ListHiddenPathsAsync(
        string scopeDirectory,
        IReadOnlyList<string> absolutePaths,
        CancellationToken cancellationToken) {
        if (absolutePaths.Count == 0) {
            return new HashSet<string>(FileSystemPathComparison.Comparer);
        }

        var candidates = absolutePaths
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .ToArray();
        // PostgreSQL string prefix semantics do not match a Windows media host. Keep the database
        // projection narrow, then bound the source set by the requested scope with the host comparer.
        var scope = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scopeDirectory));
        var sourceVisibilities = (await db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source)
            .Join(
                db.Entities.AsNoTracking(),
                file => file.EntityId,
                entity => entity.Id,
                (file, entity) => new {
                    file.Path,
                    entity.IsNsfw
                })
            .ToArrayAsync(cancellationToken))
            .Where(source => PathsOverlap(scope, source.Path))
            .ToArray();
        if (sourceVisibilities.Length == 0) {
            return new HashSet<string>(FileSystemPathComparison.Comparer);
        }

        var hidden = new HashSet<string>(FileSystemPathComparison.Comparer);
        foreach (var candidate in candidates) {
            var associatedSources = sourceVisibilities
                .Where(source => PathsOverlap(candidate, source.Path))
                .ToArray();
            if (associatedSources.Length > 0 && associatedSources.All(source => source.IsNsfw)) {
                hidden.Add(candidate);
            }
        }

        return hidden;
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> ListExcludedRelativePathsAsync(
        Guid rootId,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken) {
        if (relativePaths.Count == 0) {
            return new HashSet<string>(FileSystemPathComparison.Comparer);
        }

        var exclusions = await db.MediaFileIgnores.AsNoTracking()
            .Where(row => row.LibraryRootId == rootId)
            .Select(row => row.Path)
            .ToArrayAsync(cancellationToken);
        if (exclusions.Length == 0) {
            return new HashSet<string>(FileSystemPathComparison.Comparer);
        }

        return relativePaths
            .Select(NormalizeRelativePath)
            .Where(relativePath => exclusions.Any(excluded => IsSameOrDescendant(relativePath, excluded)))
            .ToHashSet(FileSystemPathComparison.Comparer);
    }

    /// <inheritdoc />
    public async Task UpsertExclusionAsync(
        Guid rootId,
        string relativePath,
        FileEntryKind kind,
        CancellationToken cancellationToken) {
        var path = NormalizeRelativePath(relativePath);
        var now = DateTimeOffset.UtcNow;
        var row = (await db.MediaFileIgnores
                .Where(candidate => candidate.LibraryRootId == rootId)
                .ToArrayAsync(cancellationToken))
            .FirstOrDefault(candidate => FileSystemPathComparison.Equals(candidate.Path, path));
        if (row is null) {
            db.MediaFileIgnores.Add(new MediaFileIgnoreRow {
                LibraryRootId = rootId,
                Path = path,
                Kind = kind.ToCode(),
                Reason = MediaFileIgnoreReason.ExcludedFromLibrary.ToCode(),
                CreatedAt = now,
                UpdatedAt = now
            });
        } else {
            row.Kind = kind.ToCode();
            row.Reason = MediaFileIgnoreReason.ExcludedFromLibrary.ToCode();
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveExclusionAsync(
        Guid rootId,
        string relativePath,
        CancellationToken cancellationToken) {
        var path = NormalizeRelativePath(relativePath);
        var row = (await db.MediaFileIgnores
                .Where(candidate => candidate.LibraryRootId == rootId)
                .ToArrayAsync(cancellationToken))
            .FirstOrDefault(candidate => FileSystemPathComparison.Equals(candidate.Path, path));
        if (row is null) {
            return;
        }

        db.MediaFileIgnores.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ApplyPathPrefixRewriteAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken) {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
        var now = DateTimeOffset.UtcNow;
        var files = await db.EntityFiles
            .Where(file => file.Role == EntityFileRole.Source)
            .ToArrayAsync(cancellationToken);

        foreach (var file in files) {
            if (!EntitySourcePath.TryMapPhysicalPrefix(file.Path, source, target, out var nextPath)) {
                continue;
            }

            file.Path = nextPath;
            file.UpdatedAt = now;
        }

        var capabilitySources = await db.EntitySources.ToArrayAsync(cancellationToken);
        foreach (var capabilitySource in capabilitySources) {
            if (!EntitySourcePath.TryMapPhysicalPrefix(
                    capabilitySource.Value,
                    source,
                    target,
                    out var nextPath)) {
                continue;
            }

            capabilitySource.Value = nextPath;
            capabilitySource.UpdatedAt = now;
        }

        var root = (await db.LibraryRoots.AsNoTracking().ToArrayAsync(cancellationToken))
            .FirstOrDefault(row =>
                IsSameOrDescendant(source, row.Path) ||
                IsSameOrDescendant(target, row.Path));
        if (root is not null) {
            var exclusions = await db.MediaFileIgnores
                .Where(row => row.LibraryRootId == root.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var exclusion in exclusions) {
                var absolute = Path.GetFullPath(Path.Combine(root.Path, exclusion.Path));
                if (EntitySourcePath.TryMapPhysicalPrefix(absolute, source, target, out var nextPath)) {
                    exclusion.Path = NormalizeRelativePath(Path.GetRelativePath(root.Path, nextPath));
                    exclusion.UpdatedAt = now;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool PathsOverlap(string candidatePath, string sourcePath) {
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        return FileSystemPathComparison.Equals(candidate, source) ||
            candidate.StartsWith(source + Path.DirectorySeparatorChar, FileSystemPathComparison.Comparison) ||
            source.StartsWith(candidate + Path.DirectorySeparatorChar, FileSystemPathComparison.Comparison);
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').Trim('/');

    private static bool IsSameOrDescendant(string relativePath, string excludedPath) {
        var path = NormalizeRelativePath(relativePath);
        var excluded = NormalizeRelativePath(excludedPath);
        return FileSystemPathComparison.Equals(path, excluded) ||
            path.StartsWith(excluded + "/", FileSystemPathComparison.Comparison);
    }
}
