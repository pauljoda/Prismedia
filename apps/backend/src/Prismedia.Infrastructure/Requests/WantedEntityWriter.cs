using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Requests;

/// <summary>
/// Creates wanted library entities for request commits and populates them through the shared
/// metadata-apply cascade. A wanted skeleton is a plain entity row flagged Wanted with the provider
/// external id stamped — deliberately minimal, because <see cref="ApplyProposalAsync"/> then finds it
/// external-id-first (the identify apply rule) and fills in fields, artwork, and relationships exactly
/// the way Identify would. No library root or source file is attached until the acquisition imports.
/// Kind-specific persistence (which detail row a kind carries) is the only per-kind branch here.
/// </summary>
public sealed class WantedEntityWriter(PrismediaDbContext db, EntityMetadataApplyService apply) : IWantedEntityWriter {
    /// <summary>Wanted container kinds whose empty placeholders are pruned with their last child (from the request registry).</summary>
    private static readonly HashSet<string> ContainerKindCodes = RequestKindRegistry.All
        .Where(descriptor => descriptor.IsContainer)
        .Select(descriptor => descriptor.WantedEntityKind.ToCode())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<WantedEntityResult> EnsureAsync(
        EntityKind kind, string providerId, string itemId, string title, Guid? parentEntityId, bool matchTitleKindWide, CancellationToken cancellationToken) {
        var kindCode = kind.ToCode();
        var entity = await FindByExternalIdAsync(kindCode, providerId, itemId, cancellationToken)
            ?? await FindByTitleAsync(kindCode, title, parentEntityId, matchTitleKindWide, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (entity is not null) {
            var hasFile = await HasSourceFileAsync(entity.Id, cancellationToken);

            // A title-matched entity (e.g. a scanned author/artist folder with no provider ids yet) gains
            // the provider id so every later lookup — including the import bind — resolves it id-first.
            await StampExternalIdAsync(entity.Id, providerId, itemId, now, cancellationToken);

            // Re-requesting a still-parentless wanted leaf through its container adopts it under the container.
            if (parentEntityId is { } parent && entity.ParentEntityId is null && entity.IsWanted && !hasFile) {
                entity.ParentEntityId = parent;
                entity.UpdatedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
            return new WantedEntityResult(entity.Id, Created: false, hasFile);
        }

        entity = new EntityRow {
            Id = Guid.NewGuid(),
            KindCode = kindCode,
            Title = title.Trim(),
            ParentEntityId = parentEntityId,
            IsWanted = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Entities.Add(entity);
        AddDetailRowFor(kind, entity.Id);

        await StampExternalIdAsync(entity.Id, providerId, itemId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new WantedEntityResult(entity.Id, Created: true, HasFile: false);
    }

    /// <summary>
    /// Adds the kind's detail row for a wanted skeleton, always with no library root: root-scoped stale
    /// cleanup must never touch a placeholder, and the import-time scan upsert fills in the real values
    /// (type, format, root) from what actually lands on disk. Kinds without a detail row add nothing.
    /// </summary>
    private void AddDetailRowFor(EntityKind kind, Guid entityId) {
        switch (kind) {
            case EntityKind.Book:
                // Placeholder type/format; the import corrects them from the placed file.
                db.BookDetails.Add(new BookDetailRow {
                    EntityId = entityId,
                    BookType = BookType.Novel,
                    Format = BookFormat.Epub,
                    LibraryRootId = null
                });
                break;
            case EntityKind.AudioLibrary:
                db.AudioLibraryDetails.Add(new AudioLibraryDetailRow { EntityId = entityId, LibraryRootId = null });
                break;
            case EntityKind.MusicArtist:
                db.MusicArtistDetails.Add(new MusicArtistDetailRow { EntityId = entityId, LibraryRootId = null });
                break;
            default:
                // Movie / series and other kinds carry no request-relevant detail row today.
                break;
        }
    }

    public async Task ApplyProposalAsync(Guid entityId, Contracts.Plugins.EntityMetadataProposal proposal, CancellationToken cancellationToken) {
        var fields = ProposalApplySelection.SelectAllPresentFields(proposal);
        var images = ProposalApplySelection.SelectDefaultImages(proposal);
        await apply.ApplyAsync(entityId, proposal, fields, images, cancellationToken);
    }

    public async Task<bool> DeleteIfWantedAsync(Guid entityId, CancellationToken cancellationToken) {
        var entity = await db.Entities.FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
        if (entity is null || !entity.IsWanted || await HasSourceFileAsync(entityId, cancellationToken)) {
            return false;
        }

        var parentId = entity.ParentEntityId;
        db.Entities.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);

        // A wanted container (author, artist) that just lost its last child is an empty placeholder;
        // remove it with the work (the scan's empty-grouping pruning would eventually do the same, but
        // not until the next scan).
        if (parentId is { } containerId) {
            var container = await db.Entities.FirstOrDefaultAsync(
                row => row.Id == containerId && ContainerKindCodes.Contains(row.KindCode), cancellationToken);
            if (container is { IsWanted: true } &&
                !await db.Entities.AnyAsync(row => row.ParentEntityId == containerId, cancellationToken) &&
                !await HasSourceFileAsync(containerId, cancellationToken)) {
                db.Entities.Remove(container);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return true;
    }

    public async Task<MonitorableContainer?> GetContainerAsync(Guid entityId, CancellationToken cancellationToken) {
        var entity = await db.Entities.AsNoTracking()
            .Where(row => row.Id == entityId)
            .Select(row => new { row.Id, row.KindCode, row.Title, row.ParentEntityId })
            .FirstOrDefaultAsync(cancellationToken);
        if (entity is null) {
            return null;
        }

        var providerIds = await db.EntityExternalIds.AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .OrderBy(row => row.CreatedAt)
            .Select(row => new ProviderRef(row.Provider, row.Value))
            .ToArrayAsync(cancellationToken);
        var positions = await db.EntityPositions.AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .ToDictionaryAsync(row => row.Code, row => row.Value, cancellationToken);
        var hasSource = await HasSourceFileAsync(entityId, cancellationToken);
        return new MonitorableContainer(
            entity.Id, entity.KindCode.DecodeAs<EntityKind>(), entity.Title, providerIds, hasSource,
            entity.ParentEntityId, positions);
    }

    private Task<bool> HasSourceFileAsync(Guid entityId, CancellationToken cancellationToken) =>
        db.EntityFiles.AsNoTracking()
            .AnyAsync(file => file.EntityId == entityId && file.Role == EntityFileRole.Source, cancellationToken);

    private Task<EntityRow?> FindByExternalIdAsync(string kindCode, string providerId, string itemId, CancellationToken cancellationToken) =>
        db.EntityExternalIds
            .Where(row => row.Provider == providerId && row.Value == itemId)
            .Join(db.Entities, externalId => externalId.EntityId, candidate => candidate.Id, (_, candidate) => candidate)
            .FirstOrDefaultAsync(candidate => candidate.KindCode == kindCode, cancellationToken);

    /// <summary>
    /// Title fallback, mirroring the apply cascade's scoping rules: a container grouping matches
    /// kind-wide (so a request binds to an already-scanned author/artist folder that has no provider ids
    /// yet), while a leaf only matches inside its given parent — a bare title is too weak a signal to
    /// claim a leaf globally.
    /// </summary>
    private async Task<EntityRow?> FindByTitleAsync(
        string kindCode, string title, Guid? parentEntityId, bool matchTitleKindWide, CancellationToken cancellationToken) {
        if (!matchTitleKindWide && parentEntityId is null) {
            return null;
        }

        var lowered = title.Trim().ToLower();
        return await db.Entities.FirstOrDefaultAsync(row =>
            row.KindCode == kindCode &&
            (matchTitleKindWide || row.ParentEntityId == parentEntityId) &&
            row.Title.ToLower() == lowered,
            cancellationToken);
    }

    private async Task StampExternalIdAsync(Guid entityId, string providerId, string itemId, DateTimeOffset now, CancellationToken cancellationToken) {
        var exists = await db.EntityExternalIds
            .AnyAsync(row => row.EntityId == entityId && row.Provider == providerId, cancellationToken);
        if (exists) {
            return;
        }

        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Provider = providerId,
            Value = itemId,
            Url = null,
            CreatedAt = now
        });
    }
}
