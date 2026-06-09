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

    // Resolves the local structural child a proposal targets: external-id-first, then title, scoped to
    // this parent — the shared FindEntityAsync rule used everywhere in the apply walk.
    private Task<EntityRow?> FindStructuralChildAsync(
        Guid parentEntityId,
        EntityMetadataProposal child,
        CancellationToken cancellationToken) =>
        FindEntityAsync(
            child.TargetKind.ToEntityKind().ToCode(),
            child.Patch.ExternalIds,
            child.Patch.Title,
            parentEntityId,
            cancellationToken);

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
            var image = ImageKindRoleResolver.Pick(
                images, MediaImageKind.Still, MediaImageKind.Poster, MediaImageKind.Cover, MediaImageKind.Backdrop)
                ?? images[0];
            await _artwork.DownloadPluginImageAsync(entity, image, ImageKindRoleResolver.RoleFor(image.Kind), now, cancellationToken);
        }

        entity.UpdatedAt = now;
    }
}
