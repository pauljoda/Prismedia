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
/// </summary>
public sealed class WantedEntityWriter(PrismediaDbContext db, EntityMetadataApplyService apply) : IWantedEntityWriter {
    public async Task<WantedEntityResult> EnsureAsync(
        EntityKind kind, string providerId, string itemId, string title, Guid? parentEntityId, CancellationToken cancellationToken) {
        var kindCode = kind.ToCode();
        var entity = await FindByExternalIdAsync(kindCode, providerId, itemId, cancellationToken)
            ?? await FindByTitleAsync(kind, kindCode, title, parentEntityId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (entity is not null) {
            var hasFile = await db.EntityFiles.AsNoTracking()
                .AnyAsync(file => file.EntityId == entity.Id && file.Role == EntityFileRole.Source, cancellationToken);

            // A title-matched entity (e.g. a scanned author folder with no provider ids yet) gains the
            // provider id so every later lookup — including the import bind — resolves it id-first.
            await StampExternalIdAsync(entity.Id, providerId, itemId, now, cancellationToken);

            // Re-requesting a still-parentless wanted book through its author adopts it under the author.
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

        if (kind == EntityKind.Book) {
            // Placeholder book detail with no library root: root-scoped stale cleanup never touches it,
            // and the import-time scan upsert replaces the type/format with what actually landed on disk.
            db.BookDetails.Add(new BookDetailRow {
                EntityId = entity.Id,
                BookType = BookType.Novel,
                Format = BookFormat.Epub,
                LibraryRootId = null
            });
        }

        await StampExternalIdAsync(entity.Id, providerId, itemId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new WantedEntityResult(entity.Id, Created: true, HasFile: false);
    }

    public async Task ApplyProposalAsync(Guid entityId, Contracts.Plugins.EntityMetadataProposal proposal, CancellationToken cancellationToken) {
        var fields = ProposalApplySelection.SelectAllPresentFields(proposal);
        var images = ProposalApplySelection.SelectDefaultImages(proposal);
        await apply.ApplyAsync(entityId, proposal, fields, images, cancellationToken);
    }

    private Task<EntityRow?> FindByExternalIdAsync(string kindCode, string providerId, string itemId, CancellationToken cancellationToken) =>
        db.EntityExternalIds
            .Where(row => row.Provider == providerId && row.Value == itemId)
            .Join(db.Entities, externalId => externalId.EntityId, candidate => candidate.Id, (_, candidate) => candidate)
            .FirstOrDefaultAsync(candidate => candidate.KindCode == kindCode, cancellationToken);

    /// <summary>
    /// Title fallback, mirroring the apply cascade's scoping rules: an author matches kind-wide (so a
    /// request binds to an already-scanned author folder that has no provider ids yet), while a book only
    /// matches inside the given parent — a bare title is too weak a signal to claim a book globally.
    /// </summary>
    private async Task<EntityRow?> FindByTitleAsync(
        EntityKind kind, string kindCode, string title, Guid? parentEntityId, CancellationToken cancellationToken) {
        if (kind != EntityKind.BookAuthor && parentEntityId is null) {
            return null;
        }

        var lowered = title.Trim().ToLower();
        return await db.Entities.FirstOrDefaultAsync(row =>
            row.KindCode == kindCode &&
            (kind == EntityKind.BookAuthor || row.ParentEntityId == parentEntityId) &&
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
