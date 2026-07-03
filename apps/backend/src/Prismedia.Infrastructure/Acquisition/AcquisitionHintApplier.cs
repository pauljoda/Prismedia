using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Applies an acquisition import hint to a freshly scanned book. Matches the hint to the entity by path
/// containment (the scanned book path and the hint's import path overlap), then writes the plugin/external
/// ids onto the entity so the existing identify hint resolver runs ID-first. Consuming the hint keeps it
/// from re-applying on later rescans.
///
/// Also owns the wanted-entity bind step: when the hint links a request-created wanted entity, the scan
/// calls the bind methods before its path-keyed upserts so the imported path attaches to that entity —
/// the "no duplicate on import" half of the request-builds-a-wanted-entity flow.
/// </summary>
public sealed class AcquisitionHintApplier(PrismediaDbContext db) : IAcquisitionHintApplier {
    public async Task<bool> ApplyAsync(Guid entityId, string sourcePath, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(sourcePath)) {
            return false;
        }

        var normalized = Normalize(sourcePath);
        var hints = await db.AcquisitionImportHints
            .Where(hint => !hint.Consumed)
            .ToArrayAsync(cancellationToken);

        // Most specific match wins: prefer the longest hint path that overlaps the scanned book path.
        var match = hints
            .Where(hint => PathsOverlap(normalized, Normalize(hint.SourcePath)))
            .OrderByDescending(hint => hint.SourcePath.Length)
            .FirstOrDefault();
        if (match is null) {
            return false;
        }

        var externalIds = DecodeExternalIds(match);
        if (externalIds.Count > 0) {
            var existing = await db.EntityExternalIds
                .Where(row => row.EntityId == entityId)
                .Select(row => row.Provider)
                .ToArrayAsync(cancellationToken);
            var existingProviders = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

            var now = DateTimeOffset.UtcNow;
            foreach (var (provider, value) in externalIds) {
                if (existingProviders.Contains(provider)) {
                    continue;
                }

                db.EntityExternalIds.Add(new EntityExternalIdRow {
                    Id = Guid.NewGuid(),
                    EntityId = entityId,
                    Provider = provider,
                    Value = value,
                    Url = null,
                    CreatedAt = now
                });
            }
        }

        // Record the owned source tier on the book's detail row (the format tier is derived from the row's
        // Format, never stored). This is the provenance half of the owned quality the upgrade loop compares
        // against. The scan creates the detail row before hints are applied, so it is expected to exist.
        var detail = await db.BookDetails.FirstOrDefaultAsync(row => row.EntityId == entityId, cancellationToken);
        if (detail is not null) {
            detail.SourceTier = match.OwnedSourceTier;
        }

        // NOTE: we deliberately do NOT seed the entity's description from the request here. The book's
        // description is owned by the more authoritative sources that run at/after import — the file's own
        // embedded metadata (e.g. ComicInfo) and the post-import auto-identify pass — and seeding here (before
        // the embedded-metadata step) would pre-empt the file's own description. The request-time description
        // is held on the acquisition for the request surface; the imported entity gets the better source.

        match.Consumed = true;
        match.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> BindWantedEntityAsync(EntityKind kind, string sourcePath, CancellationToken cancellationToken) {
        var entityId = await FindWantedEntityIdForPathAsync(sourcePath, cancellationToken);
        if (entityId is null) {
            return false;
        }

        // Tolerate a dangling link (the wanted entity was deleted): the scan just creates a fresh entity
        // and the ordinary hint apply still stamps its ids. Never bind an entity that already has a source.
        var kindCode = kind.ToCode();
        var entity = await db.Entities.FirstOrDefaultAsync(
            row => row.Id == entityId && row.KindCode == kindCode, cancellationToken);
        if (entity is null || await HasSourceFileAsync(entity.Id, cancellationToken)) {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        // The path is written exactly as the scan keys it, so the following upsert finds this entity.
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = entity.Id,
            Role = EntityFileRole.Source,
            Path = sourcePath,
            MimeType = ContentTypeForPath(sourcePath),
            SizeBytes = TryGetFileSize(sourcePath),
            CreatedAt = now,
            UpdatedAt = now
        });
        entity.IsWanted = false;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> BindWantedParentAsync(EntityKind parentKind, string folderPath, CancellationToken cancellationToken) {
        var entityId = await FindWantedEntityIdForPathAsync(folderPath, cancellationToken);
        if (entityId is null) {
            return false;
        }

        // The hint links the wanted LEAF (a book, an album, an episode); the grouping is an ancestor —
        // walk up until the expected kind appears (an episode's series is two levels up) and bind only
        // a fileless entity of that kind.
        var parentKindCode = parentKind.ToCode();
        var currentId = entityId;
        EntityRow? container = null;
        for (var depth = 0; currentId is { } id && depth < 4 && container is null; depth++) {
            var current = await db.Entities.AsNoTracking()
                .Where(row => row.Id == id)
                .Select(row => new { row.ParentEntityId })
                .FirstOrDefaultAsync(cancellationToken);
            if (current?.ParentEntityId is not { } ancestorId) {
                return false;
            }

            container = await db.Entities.FirstOrDefaultAsync(
                row => row.Id == ancestorId && row.KindCode == parentKindCode, cancellationToken);
            currentId = ancestorId;
        }

        if (container is null || await HasSourceFileAsync(container.Id, cancellationToken)) {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = container.Id,
            Role = EntityFileRole.Source,
            Path = folderPath,
            CreatedAt = now,
            UpdatedAt = now
        });
        container.IsWanted = false;
        container.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> BindWantedChildBySortOrderAsync(
        EntityKind childKind, string parentPath, int sortOrder, string childPath, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(childPath)) {
            return false;
        }

        // The parent is whichever entity owns the scanned parent folder — a wanted series bound moments
        // earlier in this same scan, or a real on-disk one gaining new files under a monitored tree.
        var parentId = await db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source && file.Path == parentPath)
            .Select(file => (Guid?)file.EntityId)
            .FirstOrDefaultAsync(cancellationToken);
        if (parentId is null) {
            return false;
        }

        var childKindCode = childKind.ToCode();
        var child = await db.Entities.FirstOrDefaultAsync(
            row => row.ParentEntityId == parentId && row.KindCode == childKindCode && row.IsWanted && row.SortOrder == sortOrder,
            cancellationToken);
        if (child is null || await HasSourceFileAsync(child.Id, cancellationToken)) {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        // The path is written exactly as the scan keys it, so the following upsert finds this entity.
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = child.Id,
            Role = EntityFileRole.Source,
            Path = childPath,
            MimeType = ContentTypeForPath(childPath),
            SizeBytes = TryGetFileSize(childPath),
            CreatedAt = now,
            UpdatedAt = now
        });
        child.IsWanted = false;
        child.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>The wanted-entity link of the unconsumed hint whose import path overlaps <paramref name="path"/>, or null.</summary>
    private async Task<Guid?> FindWantedEntityIdForPathAsync(string path, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        var normalized = Normalize(path);
        var hints = await db.AcquisitionImportHints
            .AsNoTracking()
            .Where(hint => !hint.Consumed && hint.EntityId != null)
            .ToArrayAsync(cancellationToken);
        return hints
            .Where(hint => PathsOverlap(normalized, Normalize(hint.SourcePath)))
            .OrderByDescending(hint => hint.SourcePath.Length)
            .Select(hint => hint.EntityId)
            .FirstOrDefault();
    }

    private Task<bool> HasSourceFileAsync(Guid entityId, CancellationToken cancellationToken) =>
        db.EntityFiles.AsNoTracking()
            .AnyAsync(file => file.EntityId == entityId && file.Role == EntityFileRole.Source, cancellationToken);

    /// <summary>Content type for a bound single-file book, mirroring what the scan stamps on creation. Null for folders/archives.</summary>
    private static string? ContentTypeForPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch {
            ".epub" => MediaContentTypes.Epub,
            ".pdf" => MediaContentTypes.Pdf,
            _ => null
        };

    private static long? TryGetFileSize(string path) {
        try { return File.Exists(path) ? new FileInfo(path).Length : null; } catch { return null; }
    }

    private static Dictionary<string, string> DecodeExternalIds(AcquisitionImportHintRow hint) {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(hint.ExternalIdsJson)) {
            var decoded = JsonSerializer.Deserialize<Dictionary<string, string>>(hint.ExternalIdsJson);
            if (decoded is not null) {
                foreach (var (provider, value) in decoded) {
                    if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(value)) {
                        ids[provider] = value;
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(hint.PluginId) && !string.IsNullOrWhiteSpace(hint.PluginItemId)) {
            ids[hint.PluginId] = hint.PluginItemId;
        }

        return ids;
    }

    private static bool PathsOverlap(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase)
        || a.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase)
        || b.StartsWith(a + "/", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimEnd('/');
}
