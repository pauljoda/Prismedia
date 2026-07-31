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
        EntityKind parentKind,
        IReadOnlyList<EntityMetadataProposal> structuralChildren,
        IReadOnlyList<EntityMetadataProposal> relationshipProposals,
        bool relationshipFieldsApplied,
        DateTimeOffset now,
        HashSet<Guid> visited,
        IReadOnlyList<string> parentPath,
        IIdentifyApplyProgressReporter? progress,
        IIdentifyTargetEligibilityService? identifyEligibility,
        CancellationToken cancellationToken) {
        if (relationshipFieldsApplied) {
            foreach (var relation in relationshipProposals) {
                if (!relation.TargetKind.IsRelationship() || string.IsNullOrWhiteSpace(relation.Patch.Title)) {
                    continue;
                }

                var linked = await FindEntityByTitleAsync(
                    relation.TargetKind.ToCode(), relation.Patch.Title.Trim(), parentEntityId: null, cancellationToken);
                if (linked is null || linked.Id == parentEntityId || !visited.Add(linked.Id)) {
                    continue;
                }

                await ApplyNodeAsync(
                    linked,
                    relation,
                    isRelationship: true,
                    now,
                    visited,
                    parentPath,
                    progress,
                    identifyEligibility,
                    cancellationToken);
                visited.Remove(linked.Id);
            }
        }

        foreach (var child in structuralChildren) {
            if (EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind)) {
                continue;
            }

            if (!EntityKindRegistry.AllowsStructuralChild(parentKind, child.TargetKind)) {
                continue;
            }

            var hasExplicitTargetEntityId = child.TargetEntityId.HasValue;
            var childEntity = child.TargetEntityId is { } existingId
                ? await _db.Entities.FirstOrDefaultAsync(row => row.Id == existingId, cancellationToken)
                : await FindStructuralChildAsync(parentEntityId, child, cancellationToken);
            if (hasExplicitTargetEntityId && childEntity is null) {
                continue;
            }

            if (childEntity is not null) {
                var persistedChildKind = childEntity.KindCode.DecodeAs<EntityKind>();
                if (persistedChildKind != child.TargetKind ||
                    !EntityKindRegistry.AllowsStructuralChild(parentKind, persistedChildKind)) {
                    continue;
                }
            }

            var resolvedPersistedChild = childEntity is not null;
            if (resolvedPersistedChild && identifyEligibility is not null &&
                !(await identifyEligibility.EvaluateAsync(childEntity!.Id, cancellationToken)).IsEligible) {
                continue;
            }

            childEntity ??= await MaterializeStructuralContainerAsync(
                parentEntityId,
                parentKind,
                child,
                now,
                identifyEligibility,
                cancellationToken);
            if (childEntity is null ||
                !await AdoptUnderParentAsync(childEntity, parentEntityId, now, cancellationToken) ||
                !visited.Add(childEntity.Id)) {
                continue;
            }

            await ApplyNodeAsync(
                childEntity,
                child,
                isRelationship: false,
                now,
                visited,
                parentPath,
                progress,
                identifyEligibility,
                cancellationToken);
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
    private async Task<EntityRow?> MaterializeStructuralContainerAsync(
        Guid parentEntityId,
        EntityKind parentKind,
        EntityMetadataProposal child,
        DateTimeOffset now,
        IIdentifyTargetEligibilityService? identifyEligibility,
        CancellationToken cancellationToken) {
        var kindCode = child.TargetKind.ToCode();
        if (!EntityKindRegistry.EnumeratesIdentifyChildren(kindCode) ||
            string.IsNullOrWhiteSpace(child.Patch.Title) ||
            !await HasAdoptableBoundStructuralDescendantAsync(
                child,
                parentEntityId,
                identifyEligibility,
                cancellationToken)) {
            return null;
        }

        var entity = CreateEntity(kindCode, child.Patch.Title.Trim(), now);
        await _structurePlacement.ValidateAsync(
            child.TargetKind,
            entity.Id,
            parentEntityId,
            currentParentId: null,
            parentKind,
            cancellationToken);
        entity.ParentEntityId = parentEntityId;
        return entity;
    }

    private async Task<bool> HasAdoptableBoundStructuralDescendantAsync(
        EntityMetadataProposal node,
        Guid proposedParentId,
        IIdentifyTargetEligibilityService? identifyEligibility,
        CancellationToken cancellationToken) {
        foreach (var child in EntityMetadataProposalTraversal.StructuralChildren(node)) {
            if (!EntityKindRegistry.AllowsStructuralChild(node.TargetKind, child.TargetKind)) {
                continue;
            }

            if (child.TargetEntityId is { } targetEntityId) {
                var target = await FindStructuralProposalTargetAsync(targetEntityId, cancellationToken);
                if (target is null) {
                    continue;
                }

                if (EntityKindRegistry.Require(target.KindCode) != child.TargetKind ||
                    (identifyEligibility is not null &&
                     !(await identifyEligibility.EvaluateAsync(target.Id, cancellationToken)).IsEligible) ||
                    !await IsCurrentParentAnAncestorOfProposedParentAsync(target, proposedParentId, cancellationToken)) {
                    continue;
                }

                return true;
            }

            if (!EntityKindRegistry.EnumeratesIdentifyChildren(child.TargetKind.ToCode()) ||
                string.IsNullOrWhiteSpace(child.Patch.Title)) {
                continue;
            }

            if (await HasAdoptableBoundStructuralDescendantAsync(
                    child,
                    proposedParentId,
                    identifyEligibility,
                    cancellationToken)) {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> IsCurrentParentAnAncestorOfProposedParentAsync(
        EntityRow child,
        Guid proposedParentId,
        CancellationToken cancellationToken) {
        if (child.Id == proposedParentId || child.ParentEntityId is not { } currentParentId) {
            return false;
        }

        var visited = new HashSet<Guid>();
        Guid? cursor = proposedParentId;
        while (cursor is { } parentId) {
            if (!visited.Add(parentId) || parentId == child.Id) {
                return false;
            }

            if (parentId == currentParentId) {
                return true;
            }

            var parent = await FindStructuralProposalTargetAsync(parentId, cancellationToken);
            if (parent is null) {
                return false;
            }

            cursor = parent.ParentEntityId;
        }

        return false;
    }

    private async Task<EntityRow?> FindStructuralProposalTargetAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var tracked = _db.ChangeTracker.Entries<EntityRow>()
            .FirstOrDefault(entry => entry.Entity.Id == entityId);
        if (tracked is not null) {
            return tracked.State == EntityState.Deleted ? null : tracked.Entity;
        }

        return await _db.Entities.AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == entityId, cancellationToken);
    }

    /// <summary>
    /// Verifies that an applied child is already beneath its proposed parent or refines its
    /// placement downward. A flat-scanned chapter can move into its newly created volume only
    /// when its current parent is an ancestor of that volume, so a title collision never steals
    /// or mutates an entity across trees.
    /// </summary>
    private async Task<bool> AdoptUnderParentAsync(EntityRow child, Guid parentEntityId, DateTimeOffset now, CancellationToken cancellationToken) {
        if (child.Id == parentEntityId) {
            return false;
        }

        if (child.ParentEntityId == parentEntityId) {
            return true;
        }

        if (child.ParentEntityId is not { }) {
            return false;
        }

        if (!await IsCurrentParentAnAncestorOfProposedParentAsync(child, parentEntityId, cancellationToken)) {
            return false;
        }

        await _structurePlacement.ValidateAsync(
            EntityKindRegistry.Require(child.KindCode),
            child.Id,
            parentEntityId,
            child.ParentEntityId,
            knownParentKind: null,
            cancellationToken);
        child.ParentEntityId = parentEntityId;
        child.UpdatedAt = now;
        return true;
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
        IIdentifyTargetEligibilityService? identifyEligibility,
        CancellationToken cancellationToken) {
        var title = !string.IsNullOrWhiteSpace(node.Patch.Title) ? node.Patch.Title.Trim() : entity.Title;
        var path = parentPath.Count == 0 ? [title] : parentPath.Concat([title]).ToArray();
        await ReportApplyProgressAsync(progress, entity.KindCode.DecodeAs<EntityKind>(), title, path, cancellationToken);

        await ApplyPatchToEntityAsync(entity, node.Patch, isRelationship ? [] : node.Images, now, cancellationToken);
        await BindProviderIdentityAsync(
            entity,
            node.Provider,
            node.Patch.ExternalIds,
            cancellationToken);
        if (isRelationship) {
            await ApplyRelationshipArtworkAsync(entity, node, now, cancellationToken);
        }

        var hasRelationshipFields = node.Patch.Credits.Count > 0
            || !string.IsNullOrWhiteSpace(node.Patch.Studio)
            || node.Patch.Tags.Count > 0;
        await ApplyChildNodesAsync(
            entity.Id,
            entity.KindCode.DecodeAs<EntityKind>(),
            EntityMetadataProposalTraversal.StructuralChildren(node),
            EntityMetadataProposalTraversal.Relationships(node),
            hasRelationshipFields,
            now,
            visited,
            path,
            progress,
            identifyEligibility,
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
            child.TargetKind.ToCode(),
            child.Patch.ExternalIds,
            child.Patch.Title,
            parentEntityId,
            cancellationToken);

    /// <summary>
    /// Resolves an unbound structural proposal exactly as the apply walk would, without creating a
    /// new provider container. Identify uses this to canonicalize current local targets before it
    /// filters stale descendants and records the accepted proposal.
    /// </summary>
    internal async Task<Guid?> ResolveExistingStructuralChildIdAsync(
        Guid parentEntityId,
        EntityMetadataProposal child,
        CancellationToken cancellationToken) =>
        (await FindStructuralChildAsync(parentEntityId, child, cancellationToken))?.Id;

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
            await UpsertExternalIdsAsync(entity.Id, patch.ExternalIds, patch.Urls, cancellationToken);
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
