using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Media.Processing;
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
public sealed partial class AcquisitionHintApplier(
    PrismediaDbContext db,
    IEntityExternalIdentityStore? externalIdentities = null,
    IEntityLifecycleMutationLease? lifecycle = null) : IAcquisitionHintApplier {
    private readonly IEntityExternalIdentityStore _externalIdentities =
        externalIdentities ?? new EfEntityExternalIdentityStore(db, TimeProvider.System);
    private readonly IEntityLifecycleMutationLease _lifecycle =
        lifecycle ?? new EfEntityLifecycleMutationLease(db, new EfEntityHierarchyReader(db));

    /// <inheritdoc />
    public async Task<Guid?> ResolveTargetEntityIdAsync(
        EntityKind kind,
        Guid acquisitionId,
        CancellationToken cancellationToken) {
        var kindCode = kind.ToCode();
        return await db.Acquisitions.AsNoTracking()
            .Where(acquisition => acquisition.Id == acquisitionId && acquisition.EntityId != null)
            .Join(
                db.Entities.AsNoTracking().Where(entity => entity.KindCode == kindCode),
                acquisition => acquisition.EntityId,
                entity => entity.Id,
                (_, entity) => (Guid?)entity.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ImportedBookPathOwner>> ResolveImportedBookOwnersAsync(
        IReadOnlyCollection<string> sourcePaths,
        CancellationToken cancellationToken) {
        var requestedPaths = sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Normalize)
            .Distinct(FileSystemPathComparison.Comparer)
            .ToArray();
        if (requestedPaths.Length == 0) {
            return [];
        }

        // Multipart audiobook hints name the exact import folder. Match that durable key exactly:
        // ancestor overlap would let a flat custom template's root-folder hint claim every root-level
        // audiobook. Path lengths bound the database projection while host-filesystem equality stays
        // in memory where the database collation cannot change its meaning.
        var requestedPathLengths = requestedPaths.Select(path => path.Length).Distinct().ToArray();
        var bookCode = EntityKind.Book.ToCode();
        var candidates = await db.AcquisitionImportHints.AsNoTracking()
            .Where(hint => hint.EntityId != null && requestedPathLengths.Contains(hint.SourcePath.Length))
            .Join(
                db.Entities.AsNoTracking().Where(entity => entity.KindCode == bookCode),
                hint => hint.EntityId,
                entity => entity.Id,
                (hint, entity) => new {
                    HintPath = hint.SourcePath,
                    BookEntityId = entity.Id,
                    hint.UpdatedAt
                })
            .ToArrayAsync(cancellationToken);

        return requestedPaths
            .Select(path => new {
                Path = path,
                Match = candidates
                    .Where(candidate => FileSystemPathComparison.Equals(
                        path,
                        Normalize(candidate.HintPath)))
                    .OrderByDescending(candidate => candidate.UpdatedAt)
                    .FirstOrDefault()
            })
            .Where(result => result.Match is not null)
            .Select(result => new ImportedBookPathOwner(result.Path, result.Match!.BookEntityId))
            .ToArray();
    }

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
        await MarkReadyForPostImportIdentifyAsync(entityId, cancellationToken);

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

    public Task<bool> BindWantedFileAsync(
        EntityKind kind,
        string filePath,
        CancellationToken cancellationToken,
        Guid? acquisitionId = null,
        bool requireExactPath = false) =>
        BindWantedPathAsync(kind, filePath, SourceBinding.File, cancellationToken, acquisitionId, requireExactPath);

    public Task<bool> BindWantedFolderAsync(
        EntityKind kind,
        string folderPath,
        CancellationToken cancellationToken,
        Guid? acquisitionId = null,
        bool requireExactPath = false) =>
        BindWantedPathAsync(kind, folderPath, SourceBinding.Folder, cancellationToken, acquisitionId, requireExactPath);

    private async Task<bool> BindWantedPathAsync(
        EntityKind kind,
        string path,
        SourceBinding binding,
        CancellationToken cancellationToken,
        Guid? acquisitionId,
        bool requireExactPath) {
        var entityId = await FindWantedEntityIdForPathAsync(
            path,
            cancellationToken,
            acquisitionId,
            exactPath: requireExactPath);
        if (entityId is null) {
            return false;
        }

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
                    var entity = await db.Entities.FirstOrDefaultAsync(
                        row => row.Id == entityId && row.KindCode == kindCode,
                        leaseCancellationToken);
                    if (entity is null || await HasBindingAsync(entity.Id, binding, leaseCancellationToken)) {
                        return;
                    }

                    var now = DateTimeOffset.UtcNow;
                    AddBinding(entity.Id, path, binding, now);
                    if (binding == SourceBinding.File) {
                        entity.IsWanted = false;
                    }
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
    public async Task<bool> BindWantedParentFolderAsync(
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
                    if (current is null || await HasBindingAsync(current.Id, SourceBinding.Folder, leaseCancellationToken)) {
                        return;
                    }

                    var now = DateTimeOffset.UtcNow;
                    AddBinding(current.Id, folderPath, SourceBinding.Folder, now);
                    current.UpdatedAt = now;
                    await db.SaveChangesAsync(leaseCancellationToken);
                    bound = true;
                },
                cancellationToken)) {
            throw new EntityLifecycleMutationConflictException(container.Id);
        }
        return bound;
    }

    public Task<Guid?> BindWantedChildFileBySortOrderAsync(
        EntityKind childKind, string parentFolderPath, int sortOrder, string filePath, CancellationToken cancellationToken) =>
        BindWantedChildPathBySortOrderAsync(childKind, parentFolderPath, sortOrder, filePath, SourceBinding.File, cancellationToken);

    public Task<Guid?> BindWantedChildFolderBySortOrderAsync(
        EntityKind childKind, string parentFolderPath, int sortOrder, string folderPath, CancellationToken cancellationToken) =>
        BindWantedChildPathBySortOrderAsync(childKind, parentFolderPath, sortOrder, folderPath, SourceBinding.Folder, cancellationToken);

    private async Task<Guid?> BindWantedChildPathBySortOrderAsync(
        EntityKind childKind,
        string parentPath,
        int sortOrder,
        string childPath,
        SourceBinding binding,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(childPath)) {
            return null;
        }

        var parentId = await FindFolderOwnerAsync(parentPath, cancellationToken);
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
                    if (current is null || await HasBindingAsync(current.Id, binding, leaseCancellationToken)) {
                        return;
                    }

                    var now = DateTimeOffset.UtcNow;
                    AddBinding(current.Id, childPath, binding, now);
                    if (binding == SourceBinding.File) {
                        current.IsWanted = false;
                    }
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

    private Task<bool> HasBindingAsync(
        Guid entityId,
        SourceBinding binding,
        CancellationToken cancellationToken) =>
        binding == SourceBinding.File
            ? HasSourceFileAsync(entityId, cancellationToken)
            : db.EntitySources.AsNoTracking().AnyAsync(
                source => source.EntityId == entityId && source.Code == EntitySourceCode.Folder.ToCode(),
                cancellationToken);

    private void AddBinding(Guid entityId, string path, SourceBinding binding, DateTimeOffset now) {
        if (binding == SourceBinding.File) {
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Role = EntityFileRole.Source,
                Path = path,
                MimeType = ContentTypeForPath(path),
                SizeBytes = TryGetFileSize(path),
                CreatedAt = now,
                UpdatedAt = now
            });
            return;
        }

        db.EntitySources.Add(new EntitySourceRow {
            EntityId = entityId,
            Code = EntitySourceCode.Folder.ToCode(),
            Value = path,
            UpdatedAt = now
        });
    }

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
            // The entity owning the imported path: exact payload-file or folder-provenance match first,
            // else the nearest ancestor folder owner —
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
            if (string.Equals(owner.KindCode, EntityKind.Book.ToCode(), StringComparison.Ordinal)
                || string.Equals(owner.KindCode, EntityKind.BookAuthor.ToCode(), StringComparison.Ordinal)) {
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
                    var linkedFilePaths = await db.EntityFiles.AsNoTracking()
                        .Where(file => file.EntityId == linkedEntityId && file.Role == EntityFileRole.Source)
                        .Select(file => file.Path)
                        .ToArrayAsync(cancellationToken);
                    var linkedFolderPaths = await db.EntitySources.AsNoTracking()
                        .Where(source => source.EntityId == linkedEntityId && source.Code == EntitySourceCode.Folder.ToCode())
                        .Select(source => source.Value)
                        .ToArrayAsync(cancellationToken);
                    if (!linkedFilePaths.Concat(linkedFolderPaths)
                        .Any(path => PathsOverlap(Normalize(path), Normalize(hint.SourcePath)))) {
                        continue;
                    }

                    identityOwnerId = linkedEntityId;
                }
            }

            StampedHintOwner? identifyRoot = null;
            if (!await _lifecycle.ExecuteAsync(
                    identityOwnerId,
                    async leaseCancellationToken => {
                        await StampExternalIdsAsync(identityOwnerId, hint, leaseCancellationToken);
                        identifyRoot = await ResolveAutoIdentifyRootAsync(
                            identityOwnerId,
                            leaseCancellationToken);
                        await MarkWantedFulfilledWhenSourceBackedAsync(
                            identityOwnerId,
                            leaseCancellationToken);
                        await MarkReadyForPostImportIdentifyAsync(
                            identifyRoot.TopLevelEntityId,
                            leaseCancellationToken);
                        hint.Consumed = true;
                        hint.UpdatedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(leaseCancellationToken);
                    },
                    cancellationToken)) {
                throw new EntityLifecycleMutationConflictException(identityOwnerId);
            }
            if (identifyRoot is not null) {
                owners.TryAdd(identifyRoot.TopLevelEntityId, identifyRoot);
            }
        }

        return owners.Values.ToArray();
    }

    /// <summary>
    /// Clears Wanted only for a definition that explicitly treats a source-backed subtree as its
    /// fulfillment boundary. Folder provenance alone intentionally does not fulfill a request.
    /// </summary>
    private async Task MarkWantedFulfilledWhenSourceBackedAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var entity = db.Entities.Local.FirstOrDefault(row => row.Id == entityId)
            ?? await db.Entities.FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
        if (entity is null || !entity.IsWanted) {
            return;
        }

        if (!EntityKindRegistry.TryDescribe(entity.KindCode, out var definition) ||
            !definition.IsFulfilledBySourceBackedSubtree) {
            return;
        }

        var subtreeIds = await new EfEntityHierarchyReader(db)
            .ListSubtreeIdsAsync(entityId, cancellationToken);
        var hasSourcePayload = subtreeIds.Count > 0 && await db.EntityFiles.AsNoTracking()
            .AnyAsync(
                file => subtreeIds.Contains(file.EntityId) && file.Role == EntityFileRole.Source,
                cancellationToken);
        if (!hasSourcePayload) {
            return;
        }

        entity.IsWanted = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The entity owning the exact payload path, else the nearest folder provenance owner.</summary>
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

            owner = await FindFolderOwnerAsync(probe, cancellationToken);
            if (owner is not null) {
                return owner;
            }

            probe = Path.GetDirectoryName(probe);
        }

        return null;
    }

    private async Task<Guid?> FindFolderOwnerAsync(string folderPath, CancellationToken cancellationToken) {
        var folderOwner = await db.EntitySources.AsNoTracking()
            .Where(source => source.Code == EntitySourceCode.Folder.ToCode() && source.Value == folderPath)
            .Select(source => (Guid?)source.EntityId)
            .FirstOrDefaultAsync(cancellationToken);
        if (folderOwner is not null) {
            return folderOwner;
        }

        // Read legacy folder rows until the guarded migration has normalized them. New bindings only
        // write EntitySource(folder), keeping the payload-file and structural-provenance shapes distinct.
        return await db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source && file.Path == folderPath)
            .Select(file => (Guid?)file.EntityId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private enum SourceBinding { File, Folder }

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

    /// <summary>
    /// The stamped entity's auto-identify root. Albums stop below their artist grouping because album
    /// identify owns track metadata; other entities walk to their top-level ancestor.
    /// </summary>
    private async Task<StampedHintOwner> ResolveAutoIdentifyRootAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
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

            var parentKindCode = await db.Entities.AsNoTracking()
                .Where(row => row.Id == parentId)
                .Select(row => row.KindCode)
                .FirstOrDefaultAsync(cancellationToken);
            if (parentKindCode is not null &&
                EntityKindRegistry.TryDescribe(parentKindCode, out var parentDefinition) &&
                parentDefinition.Identification.StopsDescendantAutoIdentifyRootTraversal) {
                break;
            }

            currentId = parentId;
        }

        return new StampedHintOwner(topLevelId, kindCode, title);
    }

    /// <summary>
    /// Real source files and their scanned children supersede the metadata-complete state of a Wanted
    /// placeholder. Clearing Organized lets the already-queued identify job hydrate episode titles,
    /// track names, and other child metadata from the stable identity stamped by the import hint.
    /// </summary>
    private async Task MarkReadyForPostImportIdentifyAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var entity = db.Entities.Local.FirstOrDefault(row => row.Id == entityId)
            ?? await db.Entities.FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
        if (entity is null || !entity.IsOrganized) {
            return;
        }

        entity.IsOrganized = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
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
