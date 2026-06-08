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
        // Project only the columns the Files page renders (id, kind, title) plus the NSFW flag used
        // to filter, rather than hydrating full entity rows for every linked source.
        var entityQuery = db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source &&
                           (file.Path == normalized || EF.Functions.Like(file.Path, normalized + Path.DirectorySeparatorChar + "%")))
            .Join(
                db.Entities.AsNoTracking(),
                file => file.EntityId,
                entity => entity.Id,
                (_, entity) => new { entity.Id, entity.KindCode, entity.Title, entity.IsNsfw })
            .Distinct();
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
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var candidates = absolutePaths
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .ToArray();
        // Every candidate lives at or under the scope directory, so only source files in that
        // subtree can own or contain one. Bounding the query here keeps the Files tab responsive on
        // large libraries instead of loading every source path in the catalog on each folder open.
        var scope = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scopeDirectory));
        var scopePrefix = scope + Path.DirectorySeparatorChar;
        var sourceVisibilities = await db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source &&
                           (file.Path == scope || EF.Functions.Like(file.Path, scopePrefix + "%")))
            .Join(
                db.Entities.AsNoTracking(),
                file => file.EntityId,
                entity => entity.Id,
                (file, entity) => new {
                    file.Path,
                    entity.IsNsfw
                })
            .ToArrayAsync(cancellationToken);
        if (sourceVisibilities.Length == 0) {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var exclusions = await db.MediaFileIgnores.AsNoTracking()
            .Where(row => row.LibraryRootId == rootId)
            .Select(row => row.Path)
            .ToArrayAsync(cancellationToken);
        if (exclusions.Length == 0) {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return relativePaths
            .Select(NormalizeRelativePath)
            .Where(relativePath => exclusions.Any(excluded => IsSameOrDescendant(relativePath, excluded)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task UpsertExclusionAsync(
        Guid rootId,
        string relativePath,
        string kind,
        CancellationToken cancellationToken) {
        var path = NormalizeRelativePath(relativePath);
        var now = DateTimeOffset.UtcNow;
        var row = await db.MediaFileIgnores.FindAsync([rootId, path], cancellationToken);
        if (row is null) {
            db.MediaFileIgnores.Add(new MediaFileIgnoreRow {
                LibraryRootId = rootId,
                Path = path,
                Kind = NormalizeKind(kind),
                Reason = "excluded-from-library",
                CreatedAt = now,
                UpdatedAt = now
            });
        } else {
            row.Kind = NormalizeKind(kind);
            row.Reason = "excluded-from-library";
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
        var row = await db.MediaFileIgnores.FindAsync([rootId, path], cancellationToken);
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
            if (!TryMapMovedPath(file.Path, source, target, out var nextPath)) {
                continue;
            }

            file.Path = nextPath;
            file.UpdatedAt = now;
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
                if (TryMapMovedPath(absolute, source, target, out var nextPath)) {
                    exclusion.Path = NormalizeRelativePath(Path.GetRelativePath(root.Path, nextPath));
                    exclusion.UpdatedAt = now;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool TryMapMovedPath(
        string currentPath,
        string sourcePath,
        string targetPath,
        out string nextPath) {
        var current = Path.GetFullPath(currentPath);
        if (string.Equals(current, sourcePath, StringComparison.OrdinalIgnoreCase)) {
            nextPath = targetPath;
            return true;
        }

        if (current.StartsWith(sourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
            nextPath = targetPath + current[sourcePath.Length..];
            return true;
        }

        nextPath = currentPath;
        return false;
    }

    private static bool PathsOverlap(string candidatePath, string sourcePath) {
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        return string.Equals(candidate, source, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(candidate + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').Trim('/');

    private static string NormalizeKind(string kind) =>
        string.Equals(kind, "directory", StringComparison.OrdinalIgnoreCase) ? "directory" : "file";

    private static bool IsSameOrDescendant(string relativePath, string excludedPath) {
        var path = NormalizeRelativePath(relativePath);
        var excluded = NormalizeRelativePath(excludedPath);
        return string.Equals(path, excluded, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(excluded + "/", StringComparison.OrdinalIgnoreCase);
    }
}
