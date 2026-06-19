using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class EntityMetadataApplyService {
    /// <summary>
    /// Walks every descendant of an applied node uniformly: its related entities (people, studios,
    /// tags) first, then its structural children, each through the single recursive
    /// <see cref="ApplyNodeAsync"/>. This is the one recursive apply routine — a structural child and a
    /// related entity are the same proposal shape and follow the same path, so a related entity can
    /// carry (and recurse into) its own structure exactly like a child.
    /// </summary>
    /// <param name="relationshipFieldsApplied">
    /// Whether the parent's scalar relationship fields (credits/studio/tags) were applied. Relationship
    /// proposals only enrich the entities those fields linked, so they are skipped when the fields were
    /// not applied (e.g. the user unticked credits, or a cascade child carried none).
    /// </param>
    private async Task ApplyChildNodesAsync(
        Guid parentEntityId,
        IReadOnlyList<EntityMetadataProposal> structuralChildren,
        IReadOnlyList<EntityMetadataProposal> relationshipProposals,
        bool relationshipFieldsApplied,
        DateTimeOffset now,
        HashSet<Guid> visited,
        IReadOnlyList<string> parentPath,
        IIdentifyApplyProgressReporter? progress,
        CancellationToken cancellationToken) {
        if (relationshipFieldsApplied) {
            foreach (var relation in relationshipProposals) {
                if (!relation.TargetKind.IsRelationship() || string.IsNullOrWhiteSpace(relation.Patch.Title)) {
                    continue;
                }

                var linked = await FindEntityByTitleAsync(
                    relation.TargetKind.ToEntityKind().ToCode(), relation.Patch.Title.Trim(), parentEntityId: null, cancellationToken);
                if (linked is null || linked.Id == parentEntityId || !visited.Add(linked.Id)) {
                    continue;
                }

                await ApplyNodeAsync(linked, relation, isRelationship: true, now, visited, parentPath, progress, cancellationToken);
                visited.Remove(linked.Id);
            }
        }

        foreach (var child in structuralChildren) {
            if (EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind)) {
                continue;
            }

            var childEntity = child.TargetEntityId is { } existingId
                ? await _db.Entities.FirstOrDefaultAsync(row => row.Id == existingId, cancellationToken)
                : await FindStructuralChildAsync(parentEntityId, child, cancellationToken)
                    ?? MaterializeStructuralContainer(parentEntityId, child, now);
            if (childEntity is null || !visited.Add(childEntity.Id)) {
                continue;
            }

            await AdoptUnderParentAsync(childEntity, parentEntityId, now, cancellationToken);
            await ApplyNodeAsync(childEntity, child, isRelationship: false, now, visited, parentPath, progress, cancellationToken);
            visited.Remove(childEntity.Id);
        }
    }

    /// <summary>
    /// Creates the entity for provider-advertised structure the library has not scanned — a
    /// volume for a book whose chapters sit flat in its folder, an unscanned season. Only an
    /// identify-container kind that adopts at least one bound local descendant is created:
    /// playable leaves and empty containers are never invented, so media files on disk remain
    /// the sole source of playable items.
    /// </summary>
    private EntityRow? MaterializeStructuralContainer(Guid parentEntityId, EntityMetadataProposal child, DateTimeOffset now) {
        var kindCode = child.TargetKind.ToEntityKind().ToCode();
        if (!EntityKindRegistry.EnumeratesIdentifyChildren(kindCode) ||
            string.IsNullOrWhiteSpace(child.Patch.Title) ||
            !HasBoundStructuralDescendant(child)) {
            return null;
        }

        var entity = CreateEntity(kindCode, child.Patch.Title.Trim(), now);
        entity.ParentEntityId = parentEntityId;
        return entity;
    }

    private static bool HasBoundStructuralDescendant(EntityMetadataProposal node) =>
        (node.Children ?? []).Any(child =>
            !EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind) &&
            (child.TargetEntityId is not null || HasBoundStructuralDescendant(child)));

    /// <summary>
    /// Moves an applied child under the parent the proposal nests it beneath. Applying structure
    /// asserts the provider's hierarchy — a flat-scanned chapter adopted by its newly created
    /// volume moves into it — but only when the child's current parent is an ancestor of the new
    /// one, so structure only ever refines downward inside its own tree and a title collision can
    /// never steal an entity across trees.
    /// </summary>
    private async Task AdoptUnderParentAsync(EntityRow child, Guid parentEntityId, DateTimeOffset now, CancellationToken cancellationToken) {
        if (child.Id == parentEntityId || child.ParentEntityId == parentEntityId || child.ParentEntityId is not { } currentParent) {
            return;
        }

        var cursor = parentEntityId;
        for (var hops = 0; hops < 32; hops++) {
            if (cursor == child.Id) {
                return;
            }

            if (cursor == currentParent) {
                child.ParentEntityId = parentEntityId;
                child.UpdatedAt = now;
                return;
            }

            var node = await _db.Entities.FindAsync([cursor], cancellationToken);
            if (node?.ParentEntityId is not { } next) {
                return;
            }

            cursor = next;
        }
    }

    /// <summary>
    /// Applies one proposal node to its resolved entity, then recurses into the node's own related
    /// entities and structural children. Descendants apply every present patch field (the cascade
    /// policy — only the accepted root honors the user's field/image selection); relationship nodes
    /// take their artwork from the relationship-aware path, structural children from their own images.
    /// </summary>
    private async Task ApplyNodeAsync(
        EntityRow entity,
        EntityMetadataProposal node,
        bool isRelationship,
        DateTimeOffset now,
        HashSet<Guid> visited,
        IReadOnlyList<string> parentPath,
        IIdentifyApplyProgressReporter? progress,
        CancellationToken cancellationToken) {
        var title = !string.IsNullOrWhiteSpace(node.Patch.Title) ? node.Patch.Title.Trim() : entity.Title;
        var path = parentPath.Count == 0 ? [title] : parentPath.Concat([title]).ToArray();
        await ReportApplyProgressAsync(progress, entity.KindCode.DecodeAs<EntityKind>(), title, path, cancellationToken);

        await ApplyPatchToEntityAsync(entity, node.Patch, isRelationship ? [] : node.Images, now, cancellationToken);
        if (isRelationship) {
            await ApplyRelationshipArtworkAsync(entity, node, now, cancellationToken);
        }

        var hasRelationshipFields = node.Patch.Credits.Count > 0
            || !string.IsNullOrWhiteSpace(node.Patch.Studio)
            || node.Patch.Tags.Count > 0;
        await ApplyChildNodesAsync(
            entity.Id,
            EntityMetadataProposalTraversal.StructuralChildren(node),
            EntityMetadataProposalTraversal.Relationships(node),
            hasRelationshipFields,
            now,
            visited,
            path,
            progress,
            cancellationToken);
    }

    private static Task ReportApplyProgressAsync(
        IIdentifyApplyProgressReporter? progress,
        EntityKind kind,
        string title,
        IReadOnlyList<string> path,
        CancellationToken cancellationToken) =>
        progress?.ReportEntityAsync(kind, title, path, cancellationToken) ?? Task.CompletedTask;

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
