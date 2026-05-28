using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class EntityMetadataApplyService {
    /// <summary>
    /// Applies cascade metadata patch fields to an existing child entity.
    /// </summary>
    private async Task ApplyStructuralChildrenAsync(
        IReadOnlyList<EntityMetadataProposal> children,
        Guid parentEntityId,
        DateTimeOffset now,
        HashSet<Guid> visited,
        CancellationToken cancellationToken) {
        foreach (var child in children) {
            if (EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind)) {
                continue;
            }

            var childEntity = child.TargetEntityId is { } existingId
                ? await _db.Entities.FirstOrDefaultAsync(row => row.Id == existingId && row.DeletedAt == null, cancellationToken)
                : await FindOrCreateStructuralChildAsync(parentEntityId, child, now, cancellationToken);

            if (childEntity is null) {
                continue;
            }

            if (!visited.Add(childEntity.Id)) {
                continue;
            }

            await ApplyPatchToEntityAsync(childEntity, child.Patch, child.Images, now, cancellationToken);
            var relationshipProposals = EntityMetadataProposalTraversal.Relationships(child);
            if (relationshipProposals.Count > 0 &&
                (child.Patch.Credits.Count > 0 || !string.IsNullOrWhiteSpace(child.Patch.Studio) || child.Patch.Tags.Count > 0)) {
                await ApplyRelationshipProposalsAsync(childEntity.Id, relationshipProposals, now, cancellationToken);
            }

            await ApplyStructuralChildrenAsync(EntityMetadataProposalTraversal.StructuralChildren(child), childEntity.Id, now, visited, cancellationToken);
            visited.Remove(childEntity.Id);
        }
    }

    private async Task<EntityRow?> FindOrCreateStructuralChildAsync(
        Guid parentEntityId,
        EntityMetadataProposal child,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(child.TargetKind) ||
            !EntityKindRegistry.TryGet(child.TargetKind, out _) ||
            string.IsNullOrWhiteSpace(child.Patch.Title)) {
            return null;
        }

        var existing = await FindStructuralChildByExternalIdsAsync(parentEntityId, child, cancellationToken)
            ?? await FindStructuralChildByTitleAsync(parentEntityId, child.TargetKind, child.Patch.Title, cancellationToken);
        if (existing is not null) {
            return existing;
        }

        var entity = new EntityRow {
            Id = Guid.NewGuid(),
            KindCode = child.TargetKind,
            Title = child.Patch.Title.Trim(),
            ParentEntityId = parentEntityId,
            SortOrder = EntityMetadataPositionRules.SortOrderFor(child.TargetKind, EntityMetadataPositionRules.Normalize(child.Patch.Positions)),
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private async Task<EntityRow?> FindStructuralChildByExternalIdsAsync(
        Guid parentEntityId,
        EntityMetadataProposal child,
        CancellationToken cancellationToken) {
        foreach (var (provider, value) in child.Patch.ExternalIds) {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            var entity = await _db.EntityExternalIds
                .Where(row => row.Provider == provider.Trim() && row.Value == value.Trim())
                .Join(
                    _db.Entities,
                    externalId => externalId.EntityId,
                    entity => entity.Id,
                    (_, entity) => entity)
                .FirstOrDefaultAsync(
                    entity => entity.ParentEntityId == parentEntityId &&
                        entity.KindCode == child.TargetKind &&
                        entity.DeletedAt == null,
                    cancellationToken);
            if (entity is not null) {
                return entity;
            }
        }

        return null;
    }

    private async Task<EntityRow?> FindStructuralChildByTitleAsync(
        Guid parentEntityId,
        string kind,
        string title,
        CancellationToken cancellationToken) {
        var normalizedTitle = title.Trim();
        return _db.Entities.Local.FirstOrDefault(row =>
                row.ParentEntityId == parentEntityId &&
                row.KindCode == kind &&
                row.Title.Equals(normalizedTitle, StringComparison.OrdinalIgnoreCase) &&
                row.DeletedAt == null)
            ?? await _db.Entities.FirstOrDefaultAsync(
                row => row.ParentEntityId == parentEntityId &&
                    row.KindCode == kind &&
                    row.Title.ToLower() == normalizedTitle.ToLower() &&
                    row.DeletedAt == null,
                cancellationToken);
    }

    private async Task ApplyPatchToEntityAsync(
        EntityRow entity,
        EntityMetadataPatch patch,
        IReadOnlyList<ImageCandidate> images,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(patch.Title)) {
            entity.Title = patch.Title.Trim();
        }

        if (!string.IsNullOrWhiteSpace(patch.Description)) {
            await UpsertDescriptionAsync(entity.Id, patch.Description, now, cancellationToken);
        }

        if (patch.ExternalIds.Count > 0) {
            await UpsertExternalIdsAsync(entity.Id, patch.ExternalIds, patch.Urls, now, cancellationToken);
        }

        if (patch.Urls.Count > 0) {
            await UpsertUrlsAsync(entity.Id, patch.Urls, now, cancellationToken);
        }

        await ApplyCascadeRelationshipFieldsAsync(entity, patch, now, cancellationToken);

        if (patch.Dates.Count > 0) {
            await UpsertDatesAsync(entity.Id, patch.Dates, now, cancellationToken);
        }

        if (patch.Stats.Count > 0) {
            await UpsertStatsAsync(entity.Id, patch.Stats, now, cancellationToken);
        }

        if (patch.Positions.Count > 0) {
            var normalizedPositions = EntityMetadataPositionRules.Normalize(patch.Positions);
            await UpsertPositionsAsync(entity, normalizedPositions, now, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(patch.Classification)) {
            await UpsertClassificationAsync(entity.Id, patch.Classification, now, cancellationToken);
        }

        if (patch.Flags is not null) {
            await UpsertFlagsAsync(entity.Id, patch.Flags, now, cancellationToken);
        }

        if (images.Count > 0) {
            var image = images.FirstOrDefault(i => i.Kind is "still") ??
                images.FirstOrDefault(i => i.Kind is "poster") ??
                images.FirstOrDefault(i => i.Kind is "cover") ??
                images.FirstOrDefault(i => i.Kind is "backdrop") ??
                images[0];
            var role = image.Kind switch {
                "still" => EntityFileRole.Thumbnail,
                "poster" => EntityFileRole.Poster,
                "cover" => EntityFileRole.Cover,
                "backdrop" => EntityFileRole.Backdrop,
                _ => EntityFileRole.Thumbnail
            };
            await _artwork.DownloadPluginImageAsync(entity, image, role, now, cancellationToken);
        }

        entity.UpdatedAt = now;
    }
}
