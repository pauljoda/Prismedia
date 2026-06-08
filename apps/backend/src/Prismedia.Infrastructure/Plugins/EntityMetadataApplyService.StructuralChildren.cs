using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
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
        IReadOnlyList<string> parentPath,
        IdentifyApplyProgressReporter? progress,
        CancellationToken cancellationToken) {
        foreach (var child in children) {
            if (EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind)) {
                continue;
            }

            var childEntity = child.TargetEntityId is { } existingId
                ? await _db.Entities.FirstOrDefaultAsync(row => row.Id == existingId, cancellationToken)
                : await FindStructuralChildAsync(parentEntityId, child, cancellationToken);

            if (childEntity is null) {
                continue;
            }

            if (!visited.Add(childEntity.Id)) {
                continue;
            }

            var childTitle = !string.IsNullOrWhiteSpace(child.Patch.Title) ? child.Patch.Title.Trim() : childEntity.Title;
            var childPath = parentPath.Count == 0 ? [childTitle] : parentPath.Concat([childTitle]).ToArray();
            progress?.ReportEntity(childEntity.KindCode.DecodeAs<EntityKind>(), childTitle, childPath);

            await ApplyPatchToEntityAsync(childEntity, child.Patch, child.Images, now, cancellationToken);
            var relationshipProposals = EntityMetadataProposalTraversal.Relationships(child);
            if (relationshipProposals.Count > 0 &&
                (child.Patch.Credits.Count > 0 || !string.IsNullOrWhiteSpace(child.Patch.Studio) || child.Patch.Tags.Count > 0)) {
                await ApplyRelationshipProposalsAsync(childEntity.Id, relationshipProposals, now, childPath, progress, cancellationToken);
            }

            await ApplyStructuralChildrenAsync(
                EntityMetadataProposalTraversal.StructuralChildren(child),
                childEntity.Id,
                now,
                visited,
                childPath,
                progress,
                cancellationToken);
            visited.Remove(childEntity.Id);
        }
    }

    private async Task<EntityRow?> FindStructuralChildAsync(
        Guid parentEntityId,
        EntityMetadataProposal child,
        CancellationToken cancellationToken) {
        var existing = await FindStructuralChildByExternalIdsAsync(parentEntityId, child, cancellationToken);
        if (existing is not null || string.IsNullOrWhiteSpace(child.Patch.Title)) {
            return existing;
        }

        return await FindStructuralChildByTitleAsync(
            parentEntityId, child.TargetKind.ToEntityKind().ToCode(), child.Patch.Title, cancellationToken);
    }

    private async Task<EntityRow?> FindStructuralChildByExternalIdsAsync(
        Guid parentEntityId,
        EntityMetadataProposal child,
        CancellationToken cancellationToken) {
        var kindCode = child.TargetKind.ToEntityKind().ToCode();
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
                        entity.KindCode == kindCode,
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
                row.Title.Equals(normalizedTitle, StringComparison.OrdinalIgnoreCase))
            ?? await _db.Entities.FirstOrDefaultAsync(
                row => row.ParentEntityId == parentEntityId &&
                    row.KindCode == kind &&
                    row.Title.ToLower() == normalizedTitle.ToLower(),
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
