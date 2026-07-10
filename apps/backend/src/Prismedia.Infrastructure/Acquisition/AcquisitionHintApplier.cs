using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
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
/// <param name="db">Scoped Prismedia unit of work.</param>
/// <param name="externalIdentities">
/// Canonical identity store. Direct test construction may omit it to use the EF implementation over
/// <paramref name="db"/>.
/// </param>
public sealed class AcquisitionHintApplier(
    PrismediaDbContext db,
    IEntityExternalIdentityStore? externalIdentities = null,
    IEntityLifecycleMutationLease? lifecycle = null) : IAcquisitionHintApplier {
    private readonly IEntityExternalIdentityStore _externalIdentities =
        externalIdentities ?? new EfEntityExternalIdentityStore(db, TimeProvider.System);
    private readonly IEntityLifecycleMutationLease _lifecycle =
        lifecycle ?? new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db));

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

        if (!await _lifecycle.ExecuteAsync(
                entityId,
                leaseCancellationToken => ApplyHintWithinLifecycleAsync(
                    entityId,
                    match,
                    leaseCancellationToken),
                cancellationToken)) {
            throw new EntityLifecycleMutationConflictException(entityId);
        }
        return true;
    }

    private async Task ApplyHintWithinLifecycleAsync(
        Guid entityId,
        AcquisitionImportHintRow match,
        CancellationToken cancellationToken) {
        await StampExternalIdsAsync(entityId, match, cancellationToken);

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
    }

    public async Task<bool> BindWantedEntityAsync(
        EntityKind kind,
        string sourcePath,
        CancellationToken cancellationToken,
        Guid? acquisitionId = null,
        bool requireExactPath = false) {
        // TV callers require an exact hint path: broad checkpoint hints exist to protect structural
        // parent/position binding and must never attach S03 to the first S01 file. Other media retain
        // their established folder-overlap binding semantics.
        var entityId = await FindWantedEntityIdForPathAsync(
            sourcePath,
            cancellationToken,
            acquisitionId,
            exactPath: requireExactPath);
        if (entityId is null) {
            return false;
        }

        // ExecuteAsync deliberately returns false for both a destructive lifecycle owner and a missing
        // Entity. A dangling hint is an ordinary scan race (the scanner should create a fresh Entity),
        // while a still-existing claimed target must retry after cleanup. Preserve that distinction.
        if (!await db.Entities.AsNoTracking().AnyAsync(
                row => row.Id == entityId.Value,
                cancellationToken)) {
            return false;
        }

        var kindCode = kind.ToCode();
        var bound = false;
        if (!await _lifecycle.ExecuteAsync(
                entityId.Value,
                async leaseCancellationToken => {
                    // Tolerate a dangling link (the wanted entity was deleted): the scan creates fresh.
                    // Never bind an Entity that already has a source.
                    var entity = await db.Entities.FirstOrDefaultAsync(
                        row => row.Id == entityId && row.KindCode == kindCode,
                        leaseCancellationToken);
                    if (entity is null || await HasSourceFileAsync(entity.Id, leaseCancellationToken)) {
                        return;
                    }

                    var now = DateTimeOffset.UtcNow;
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
                    await db.SaveChangesAsync(leaseCancellationToken);
                    bound = true;
                },
                cancellationToken)) {
            if (!await db.Entities.AsNoTracking().AnyAsync(
                    row => row.Id == entityId.Value,
                    cancellationToken)) {
                return false;
            }

            throw new EntityLifecycleMutationConflictException(entityId.Value);
        }
        return bound;
    }

    public async Task<bool> BindWantedParentAsync(
        EntityKind parentKind,
        string folderPath,
        CancellationToken cancellationToken,
        Guid? acquisitionId = null) {
        var entityId = await FindWantedEntityIdForPathAsync(
            folderPath,
            cancellationToken,
            acquisitionId,
            exactPath: false);
        if (entityId is null) {
            return false;
        }

        // The hint links the wanted LEAF (a book, an album, an episode); the grouping is an ancestor —
        // walk up until the expected kind appears (an episode's series is two levels up) and bind only
        // a fileless entity of that kind.
        var parentKindCode = parentKind.ToCode();
        var currentId = entityId;
        var visited = new HashSet<Guid>();
        EntityRow? container = null;
        while (currentId is { } id && visited.Add(id) && container is null) {
            var current = await db.Entities.AsNoTracking()
                .Where(row => row.Id == id)
                .Select(row => new { row.ParentEntityId })
                .FirstOrDefaultAsync(cancellationToken);
            if (current?.ParentEntityId is not { } ancestorId) {
                return false;
            }

            var ancestor = await db.Entities.FirstOrDefaultAsync(
                row => row.Id == ancestorId,
                cancellationToken);
            if (ancestor is null) {
                return false;
            }

            if (ancestor.KindCode == parentKindCode) {
                container = ancestor;
            }
            currentId = ancestorId;
        }

        if (container is null) {
            return false;
        }
        var bound = false;
        if (!await _lifecycle.ExecuteAsync(
                container.Id,
                async leaseCancellationToken => {
                    var current = await db.Entities.FirstOrDefaultAsync(
                        row => row.Id == container.Id && row.KindCode == parentKindCode,
                        leaseCancellationToken);
                    if (current is null || await HasSourceFileAsync(current.Id, leaseCancellationToken)) {
                        return;
                    }

                    var now = DateTimeOffset.UtcNow;
                    db.EntityFiles.Add(new EntityFileRow {
                        Id = Guid.NewGuid(),
                        EntityId = current.Id,
                        Role = EntityFileRole.Source,
                        Path = folderPath,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    current.IsWanted = false;
                    current.UpdatedAt = now;
                    await db.SaveChangesAsync(leaseCancellationToken);
                    bound = true;
                },
                cancellationToken)) {
            throw new EntityLifecycleMutationConflictException(container.Id);
        }
        return bound;
    }

    public async Task<Guid?> BindWantedChildBySortOrderAsync(
        EntityKind childKind, string parentPath, int sortOrder, string childPath, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(childPath)) {
            return null;
        }

        // The parent is whichever entity owns the scanned parent folder — a wanted series bound moments
        // earlier in this same scan, or a real on-disk one gaining new files under a monitored tree.
        var parentId = await db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source && file.Path == parentPath)
            .Select(file => (Guid?)file.EntityId)
            .FirstOrDefaultAsync(cancellationToken);
        if (parentId is null) {
            return null;
        }

        var childKindCode = childKind.ToCode();
        var child = await db.Entities.FirstOrDefaultAsync(
            row => row.ParentEntityId == parentId && row.KindCode == childKindCode && row.IsWanted && row.SortOrder == sortOrder,
            cancellationToken);
        if (child is null) {
            return null;
        }
        Guid? boundId = null;
        if (!await _lifecycle.ExecuteAsync(
                child.Id,
                async leaseCancellationToken => {
                    var current = await db.Entities.FirstOrDefaultAsync(
                        row => row.Id == child.Id
                            && row.ParentEntityId == parentId
                            && row.KindCode == childKindCode
                            && row.IsWanted
                            && row.SortOrder == sortOrder,
                        leaseCancellationToken);
                    if (current is null || await HasSourceFileAsync(current.Id, leaseCancellationToken)) {
                        return;
                    }

                    var now = DateTimeOffset.UtcNow;
                    db.EntityFiles.Add(new EntityFileRow {
                        Id = Guid.NewGuid(),
                        EntityId = current.Id,
                        Role = EntityFileRole.Source,
                        Path = childPath,
                        MimeType = ContentTypeForPath(childPath),
                        SizeBytes = TryGetFileSize(childPath),
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    current.IsWanted = false;
                    current.UpdatedAt = now;
                    await db.SaveChangesAsync(leaseCancellationToken);
                    boundId = current.Id;
                },
                cancellationToken)) {
            throw new EntityLifecycleMutationConflictException(child.Id);
        }
        return boundId;
    }

    /// <summary>The wanted-entity link of the unconsumed hint whose import path overlaps <paramref name="path"/>, or null.</summary>
    private async Task<Guid?> FindWantedEntityIdForPathAsync(
        string path,
        CancellationToken cancellationToken,
        Guid? acquisitionId,
        bool exactPath) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        var normalized = Normalize(path);
        var hintsQuery = db.AcquisitionImportHints
            .AsNoTracking()
            .Where(hint => !hint.Consumed && hint.EntityId != null);
        if (acquisitionId is { } scopedAcquisitionId) {
            hintsQuery = hintsQuery.Where(hint => hint.AcquisitionId == scopedAcquisitionId);
        }

        var hints = await hintsQuery.ToArrayAsync(cancellationToken);
        return hints
            .Where(hint => exactPath
                ? FileSystemPathComparison.Equals(normalized, Normalize(hint.SourcePath))
                : PathsOverlap(normalized, Normalize(hint.SourcePath)))
            .OrderByDescending(hint => hint.SourcePath.Length)
            .Select(hint => hint.EntityId)
            .FirstOrDefault();
    }

    private Task<bool> HasSourceFileAsync(Guid entityId, CancellationToken cancellationToken) =>
        db.EntityFiles.AsNoTracking()
            .AnyAsync(file => file.EntityId == entityId && file.Role == EntityFileRole.Source, cancellationToken);

    public async Task<IReadOnlyList<StampedHintOwner>> ApplyToFolderOwnersAsync(
        CancellationToken cancellationToken,
        Guid? acquisitionId = null) {
        var hintsQuery = db.AcquisitionImportHints.Where(hint => !hint.Consumed);
        if (acquisitionId is { } scopedAcquisitionId) {
            hintsQuery = hintsQuery.Where(hint => hint.AcquisitionId == scopedAcquisitionId);
        }

        var hints = await hintsQuery.ToArrayAsync(cancellationToken);
        if (hints.Length == 0) {
            return [];
        }

        var owners = new Dictionary<Guid, StampedHintOwner>();
        foreach (var hint in hints) {
            // The entity owning the imported path: exact Source match first (the season/album folder or
            // the episode/movie file the scan keyed), else the nearest ancestor folder that owns one —
            // a merged import's hint may name a freshly created folder inside an existing tree.
            var entityId = await FindOwnerBySourcePathAsync(hint.SourcePath, cancellationToken);
            if (entityId is null) {
                continue; // not scanned yet — the hint stays for a later pass
            }

            var owner = await db.Entities.AsNoTracking()
                .Where(row => row.Id == entityId)
                .Select(row => new { row.Id, row.KindCode })
                .FirstOrDefaultAsync(cancellationToken);
            if (owner is null) {
                continue;
            }

            // Book hints keep the book scan's ApplyAsync path (which also records the owned source tier).
            if (string.Equals(owner.KindCode, EntityKindRegistry.Book.Code, StringComparison.Ordinal)
                || string.Equals(owner.KindCode, EntityKindRegistry.BookAuthor.Code, StringComparison.Ordinal)) {
                continue;
            }

            // A path can deliberately be broader than the acquired Entity while an import is
            // checkpointed (for example a series folder protecting a season-pack move). When the hint
            // links a real Entity, stamp THAT Entity after its Source binding succeeds instead of
            // leaking a season/episode identity onto the broad folder owner. A dangling link falls back
            // to the path owner; an existing-but-still-fileless link leaves the hint for a later pass.
            var identityOwnerId = owner.Id;
            if (hint.EntityId is { } linkedEntityId) {
                var linkedExists = await db.Entities.AsNoTracking()
                    .AnyAsync(row => row.Id == linkedEntityId, cancellationToken);
                if (linkedExists) {
                    var linkedPaths = await db.EntityFiles.AsNoTracking()
                        .Where(file => file.EntityId == linkedEntityId && file.Role == EntityFileRole.Source)
                        .Select(file => file.Path)
                        .ToArrayAsync(cancellationToken);
                    if (!linkedPaths.Any(path => PathsOverlap(Normalize(path), Normalize(hint.SourcePath)))) {
                        continue;
                    }

                    identityOwnerId = linkedEntityId;
                }
            }

            StampedHintOwner? top = null;
            if (!await _lifecycle.ExecuteAsync(
                    identityOwnerId,
                    async leaseCancellationToken => {
                        await StampExternalIdsAsync(identityOwnerId, hint, leaseCancellationToken);
                        hint.Consumed = true;
                        hint.UpdatedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(leaseCancellationToken);
                        top = await ResolveTopLevelAsync(identityOwnerId, leaseCancellationToken);
                    },
                    cancellationToken)) {
                throw new EntityLifecycleMutationConflictException(identityOwnerId);
            }
            if (top is not null) {
                owners.TryAdd(top.TopLevelEntityId, top);
            }
        }

        return owners.Values.ToArray();
    }

    /// <summary>The entity owning the exact path, else the nearest ancestor folder with a Source row.</summary>
    private async Task<Guid?> FindOwnerBySourcePathAsync(string sourcePath, CancellationToken cancellationToken) {
        var probe = sourcePath;
        var visited = new HashSet<string>(FileSystemPathComparison.Comparer);
        while (!string.IsNullOrEmpty(probe) && visited.Add(probe)) {
            var owner = await db.EntityFiles.AsNoTracking()
                .Where(file => file.Role == EntityFileRole.Source && file.Path == probe)
                .Select(file => (Guid?)file.EntityId)
                .FirstOrDefaultAsync(cancellationToken);
            if (owner is not null) {
                return owner;
            }

            probe = Path.GetDirectoryName(probe);
        }

        return null;
    }

    /// <summary>Writes the hint's external/plugin ids onto the entity, skipping providers it already carries.</summary>
    private async Task StampExternalIdsAsync(Guid entityId, AcquisitionImportHintRow hint, CancellationToken cancellationToken) {
        var externalIds = DecodeExternalIds(hint);
        if (externalIds.Count == 0) {
            return;
        }

        await _externalIdentities.WriteAsync(
            entityId,
            externalIds,
            ExternalIdentityWriteMode.AddMissing,
            cancellationToken);
    }

    /// <summary>The stamped entity's top-level ancestor (a series, an artist, the movie itself) for the identify kick.</summary>
    private async Task<StampedHintOwner> ResolveTopLevelAsync(Guid entityId, CancellationToken cancellationToken) {
        var currentId = entityId;
        var topLevelId = entityId;
        var kindCode = string.Empty;
        var title = string.Empty;
        var visited = new HashSet<Guid>();
        while (visited.Add(currentId)) {
            var current = await db.Entities.AsNoTracking()
                .Where(row => row.Id == currentId)
                .Select(row => new { row.KindCode, row.Title, row.ParentEntityId })
                .FirstOrDefaultAsync(cancellationToken);
            if (current is null) {
                break;
            }

            topLevelId = currentId;
            kindCode = current.KindCode;
            title = current.Title;
            if (current.ParentEntityId is not { } parentId || visited.Contains(parentId)) {
                break;
            }

            currentId = parentId;
        }

        return new StampedHintOwner(topLevelId, kindCode, title);
    }

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

    private static IReadOnlyCollection<EntityExternalId> DecodeExternalIds(AcquisitionImportHintRow hint) {
        var ids = new Dictionary<string, EntityExternalId>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(hint.ExternalIdsJson)) {
            var decoded = JsonSerializer.Deserialize<Dictionary<string, string>>(hint.ExternalIdsJson);
            if (decoded is not null) {
                foreach (var (provider, value) in decoded) {
                    AddIfValid(ids, provider, value);
                }
            }
        }

        AddIfValid(ids, hint.IdentityNamespace, hint.IdentityValue);

        return ids.Values.ToArray();
    }

    private static void AddIfValid(
        IDictionary<string, EntityExternalId> identities,
        string? identityNamespace,
        string? value) {
        if (string.IsNullOrWhiteSpace(identityNamespace) || string.IsNullOrWhiteSpace(value)) {
            return;
        }

        try {
            var association = new EntityExternalId(new ExternalIdentity(identityNamespace, value));
            identities[association.Identity.Namespace] = association;
        } catch (ArgumentException) {
            // Acquisition hints can carry transient search URLs alongside persistent ids. Invalid
            // identity-shaped values are intentionally ignored instead of aborting the import scan.
        }
    }

    private static bool PathsOverlap(string a, string b) =>
        FileSystemPathComparison.Equals(a, b)
        || a.StartsWith(b + "/", FileSystemPathComparison.Comparison)
        || b.StartsWith(a + "/", FileSystemPathComparison.Comparison);

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimEnd('/');
}
