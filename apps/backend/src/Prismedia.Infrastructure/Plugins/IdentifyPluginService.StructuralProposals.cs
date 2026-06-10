using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Structural-proposal construction, child matching/merging, and entity snapshot helpers
/// for <see cref="IdentifyPluginService"/>.
/// </summary>
public sealed partial class IdentifyPluginService {
    private async Task<EntityMetadataProposal> BuildStructuralProposalAsync(
        EntityRow entity,
        EntityMetadataProposal providerProposal,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        IReadOnlyList<IdentifyEntitySnapshot> ancestorPath,
        bool includeNsfw,
        HashSet<Guid> visited,
        CancellationToken cancellationToken,
        bool cascadeChildren = true,
        IIdentifyCascadeSink? sink = null) {
        // Bind the children the provider already returned in its own proposal to local entities.
        var boundProviderProposal = await BindLocalStructuralTargetsAsync(providerProposal, entity.Id, cancellationToken);
        var titledProposal = EnsureMeaningfulTitle(boundProviderProposal, entity.Title);

        // Base children for every streamed root: relationships (cast, studios, tags) plus any provider
        // child that has no local entity yet (e.g. an unscanned season) so it can be materialized.
        // Crucially we exclude provider children already bound to a LOCAL entity here: those only
        // appear once the cascade has fully resolved them below, so a child being present in the
        // streamed proposal reliably means it is done — which is what the review grid keys off. Without
        // this, a provider that returns its whole tree up front (e.g. MangaDex volumes, a TMDB season)
        // would show every child as "matched" instantly while the cascade was still resolving their
        // own children, with no per-child progress.
        var baseChildren = (titledProposal.Children ?? [])
            .Where(child =>
                EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind) ||
                child.TargetEntityId is null)
            .ToArray();

        // Builds the root proposal shape from the children resolved so far — used both for the final
        // return and for each partial-root the streaming sink publishes.
        EntityMetadataProposal Root(IReadOnlyList<EntityMetadataProposal> structural) => titledProposal with {
            TargetKind = entity.KindCode.DecodeAs<ProposalKind>(),
            TargetEntityId = entity.Id,
            Children = MergeStructuralChildren(baseChildren, structural),
            Relationships = EntityMetadataProposalTraversal.Relationships(titledProposal)
        };

        // Seed the sink with the parent (no local children yet) so the queue item shows it immediately
        // with a stable ProposalId; children stream in one at a time as the cascade resolves each.
        if (sink is not null) {
            await SafeFlushAsync(sink, Root([]), cancellationToken);
        }

        // Walk the local structural children and identify each one with this entity's context threaded
        // down (its freshly-identified external ids as an ancestor, plus the child's structural
        // position such as a season number). This is the recursive full-tree cascade: a series fills
        // its seasons and episodes, a book fills its volumes and chapters, etc. Only identify-container
        // kinds cascade — a leaf-content kind (a movie wrapping a single video) is one work and never
        // walks into its own media file. Child recursions never flush (sink: null) so only the root
        // streams; each child's full subtree is resolved before it is merged into the root.
        var structuralChildren = new List<EntityMetadataProposal>();
        if (cascadeChildren && EntityKindRegistry.EnumeratesIdentifyChildren(entity.KindCode)) {
            var existingChildren = await LoadStructuralChildrenAsync(entity.Id, cancellationToken);
            var providerStructuralChildren = EntityMetadataProposalTraversal.StructuralChildren(boundProviderProposal);
            foreach (var child in existingChildren) {
                // Sanity-check before each child that the streaming destination is still live. If the
                // user removed this item from the queue (or a newer search superseded it), the sink
                // reports inactive and we drop the rest of the walk — otherwise an orphaned background
                // cascade keeps resolving children and re-populates the removed item, popping it back
                // into the queue and conflicting with the next time it is queued.
                if (sink is not null && !await sink.IsActiveAsync(cancellationToken)) {
                    break;
                }

                if (!SupportsKind(descriptor.Manifest, child.Entity.KindCode)) {
                    continue;
                }

                var providerChild = providerStructuralChildren.FirstOrDefault(proposal => IsSameStructuralChild(child, proposal));
                if (providerChild is not null) {
                    structuralChildren.Add(await HydrateMatchedProviderChildAsync(
                        child, providerChild, descriptor, auth, ancestorPath, includeNsfw, visited, cancellationToken));
                } else {
                    var childResponse = await IdentifyEntityWithStructuralContextAsync(
                        child.Entity, descriptor, auth, query: null, ancestors: ancestorPath,
                        parentSortOrder: child.SortOrder, includeNsfw, visited, cancellationToken,
                        cascadeChildren: true, sink: null);
                    if (childResponse.Ok && childResponse.Result?.Patch is not null) {
                        structuralChildren.Add(EnsureStructuralPositions(childResponse.Result, child));
                    } else {
                        continue;
                    }
                }

                if (sink is not null) {
                    await SafeFlushAsync(sink, Root(structuralChildren), cancellationToken);
                }
            }
        }

        return Root(structuralChildren);
    }

    /// <summary>Publishes a partial root to the cascade sink, swallowing failures so streaming never aborts the cascade.</summary>
    private static async Task SafeFlushAsync(IIdentifyCascadeSink sink, EntityMetadataProposal partialRoot, CancellationToken cancellationToken) {
        try {
            await sink.OnEntityResolvedAsync(partialRoot, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch {
            // Best-effort streaming; the final proposal is still returned and persisted at job completion.
        }
    }

    /// <summary>
    /// Hydrates a provider-returned child shell against the local entity. If the local child has its
    /// own supported structural descendants that the provider shell did not fully cover, re-identify
    /// the child so the cascade fills them in; otherwise the provider's version is already complete.
    /// </summary>
    private async Task<EntityMetadataProposal> HydrateMatchedProviderChildAsync(
        StructuralChild child,
        EntityMetadataProposal providerChild,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        IReadOnlyList<IdentifyEntitySnapshot> ancestorPath,
        bool includeNsfw,
        HashSet<Guid> visited,
        CancellationToken cancellationToken) {
        if (!await HasSupportedStructuralChildrenAsync(child.Entity.Id, descriptor.Manifest, cancellationToken)) {
            return providerChild;
        }

        var providerStructuralChildren = EntityMetadataProposalTraversal.StructuralChildren(providerChild);
        if (providerStructuralChildren.Count > 0 &&
            !await HasMissingSupportedStructuralChildrenAsync(
                child.Entity.Id, providerStructuralChildren, descriptor.Manifest, cancellationToken)) {
            return providerChild;
        }

        var childResponse = await IdentifyEntityWithStructuralContextAsync(
            child.Entity, descriptor, auth, query: null, ancestors: ancestorPath,
            parentSortOrder: child.SortOrder, includeNsfw, visited, cancellationToken);
        return childResponse.Ok && childResponse.Result?.Patch is not null
            ? EnsureStructuralPositions(childResponse.Result, child)
            : providerChild;
    }

    private async Task<bool> HasSupportedStructuralChildrenAsync(
        Guid parentEntityId,
        PluginManifest manifest,
        CancellationToken cancellationToken) {
        var childKinds = await _db.Entities
            .AsNoTracking()
            .Where(row => row.ParentEntityId == parentEntityId)
            .Select(row => row.KindCode)
            .ToArrayAsync(cancellationToken);
        return childKinds.Any(kind => manifest.Supports.Any(support =>
            support.EntityKind.TryDecodeAs<ProposalKind>(out var supportKind) &&
            IsCompatibleStructuralKind(kind, supportKind)));
    }

    private async Task<bool> HasMissingSupportedStructuralChildrenAsync(
        Guid parentEntityId,
        IReadOnlyList<EntityMetadataProposal> providerChildren,
        PluginManifest manifest,
        CancellationToken cancellationToken) {
        var localChildren = await LoadStructuralChildrenAsync(parentEntityId, cancellationToken);
        return localChildren
            .Where(child => manifest.Supports.Any(support =>
                support.EntityKind.TryDecodeAs<ProposalKind>(out var supportKind) &&
                IsCompatibleStructuralKind(child.Entity.KindCode, supportKind)))
            .Any(child => !providerChildren.Any(providerChild =>
                providerChild.TargetEntityId == child.Entity.Id ||
                IsSameStructuralChild(child, providerChild)));
    }

    private static IReadOnlyList<EntityMetadataProposal> MergeStructuralChildren(
        IReadOnlyList<EntityMetadataProposal> providerChildren,
        IReadOnlyList<EntityMetadataProposal> localChildren) {
        // Single-source lists still normalize: duplicate nodes for the same target entity would
        // otherwise apply twice and write colliding titles and sort orders.
        if (providerChildren.Count == 0) {
            return NormalizeStructuralChildren(localChildren);
        }

        if (localChildren.Count == 0) {
            return NormalizeStructuralChildren(providerChildren);
        }

        var merged = new List<EntityMetadataProposal>(providerChildren);
        foreach (var localChild in localChildren) {
            var existingIndex = merged.FindIndex(providerChild => IsSameStructuralChild(providerChild, localChild));
            if (existingIndex >= 0) {
                merged[existingIndex] = localChild;
                continue;
            }

            merged.Add(localChild);
        }

        return NormalizeStructuralChildren(merged);
    }

    private static IReadOnlyList<EntityMetadataProposal> NormalizeStructuralChildren(
        IReadOnlyList<EntityMetadataProposal> children) =>
        children
            .Select((child, index) => new { Child = child, Index = index })
            .GroupBy(row => StructuralChildKey(row.Child), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(row => StructuralSortOrder(row.Child) is null)
            .ThenBy(row => StructuralSortOrder(row.Child))
            .ThenBy(row => row.Index)
            .Select(row => row.Child)
            .ToArray();

    private static string StructuralChildKey(EntityMetadataProposal child) {
        if (child.TargetEntityId is { } targetEntityId) {
            return $"id:{targetEntityId}";
        }

        var sortOrder = StructuralSortOrder(child);
        if (sortOrder is not null) {
            return $"position:{StructuralKindKey(child.TargetKind)}:{sortOrder}";
        }

        if (!string.IsNullOrWhiteSpace(child.ProposalId)) {
            return $"proposal:{child.ProposalId}";
        }

        return $"title:{StructuralKindKey(child.TargetKind)}:{child.Patch.Title?.Trim()}";
    }

    private static int? StructuralSortOrder(EntityMetadataProposal child) =>
        EntityMetadataPositionRules.SortOrderFor(
            child.TargetKind.ToEntityKind().ToCode(),
            EntityMetadataPositionRules.Normalize(child.Patch.Positions));

    // The structural key collapses a proposal kind to the entity kind it persists as, so a
    // provider's "video-episode" leaf and a local "video" sort/dedup into the same bucket.
    private static string StructuralKindKey(ProposalKind kind) =>
        kind.ToEntityKind().ToCode();

    private static bool IsSameStructuralChild(EntityMetadataProposal left, EntityMetadataProposal right) {
        if (!AreCompatibleProposalKinds(left.TargetKind, right.TargetKind)) {
            return false;
        }

        if (left.TargetEntityId is { } leftId && right.TargetEntityId is { } rightId) {
            return leftId == rightId;
        }

        var leftSortOrder = StructuralSortOrder(left);
        var rightSortOrder = StructuralSortOrder(right);
        if (leftSortOrder is not null || rightSortOrder is not null) {
            return leftSortOrder is not null &&
                rightSortOrder is not null &&
                leftSortOrder == rightSortOrder;
        }

        return !string.IsNullOrWhiteSpace(left.Patch.Title) &&
            !string.IsNullOrWhiteSpace(right.Patch.Title) &&
            left.Patch.Title.Equals(right.Patch.Title, StringComparison.OrdinalIgnoreCase);
    }

    // Two proposal kinds are compatible when they persist as the same entity kind — this is what
    // makes a provider's "video-episode" leaf match a local "video" (both map to EntityKind.Video).
    private static bool AreCompatibleProposalKinds(ProposalKind leftKind, ProposalKind rightKind) =>
        leftKind.ToEntityKind() == rightKind.ToEntityKind();

    /// <summary>
    /// Guards against a proposal whose title is just the provider's own identifier. Some providers
    /// fall back to the raw external id for the title when a detail lookup returns nothing (e.g. a
    /// transient upstream failure), which would otherwise surface — and apply — a bare GUID as the
    /// entity's name. When the proposed title is empty or equals one of the proposal's own external
    /// id values, fall back to the local entity's existing title so the UI and apply never write a
    /// raw id. This is provider-agnostic: any plugin that degrades this way is covered.
    /// </summary>
    private static EntityMetadataProposal EnsureMeaningfulTitle(EntityMetadataProposal proposal, string localTitle) {
        var patch = proposal.Patch;
        if (patch is null || string.IsNullOrWhiteSpace(localTitle)) {
            return proposal;
        }

        var title = patch.Title?.Trim();
        var titleIsRawId = string.IsNullOrEmpty(title) ||
            (patch.ExternalIds is not null &&
             patch.ExternalIds.Values.Any(value => string.Equals(value, title, StringComparison.OrdinalIgnoreCase)));
        return titleIsRawId
            ? proposal with { Patch = patch with { Title = localTitle } }
            : proposal;
    }

    private async Task<EntityMetadataProposal> BindLocalStructuralTargetsAsync(
        EntityMetadataProposal proposal,
        Guid parentEntityId,
        CancellationToken cancellationToken) {
        var proposalChildren = proposal.Children ?? [];
        if (proposalChildren.Count == 0) {
            return proposal;
        }

        var localChildren = await LoadStructuralChildrenAsync(parentEntityId, cancellationToken);
        var children = new List<EntityMetadataProposal>(proposalChildren.Count);
        foreach (var childProposal in proposalChildren) {
            if (EntityMetadataProposalTraversal.IsRelationshipKind(childProposal.TargetKind)) {
                children.Add(childProposal);
                continue;
            }

            var localChild = localChildren.FirstOrDefault(child => IsSameStructuralChild(child, childProposal));
            if (localChild is null) {
                // No local entity matches this provider child. Preserve it unbound only when the
                // provider advertised it as a structural container (e.g. a season the library has
                // not scanned yet) so it can be proposed as a new child. Leaf children the provider
                // cascaded (episodes, etc.) are dropped — we never invent playable items that have
                // no local media file.
                if (IsProviderAdvertisedStructuralChild(childProposal)) {
                    children.Add(childProposal);
                }

                continue;
            }

            var boundChild = await BindLocalStructuralTargetsAsync(
                childProposal with { TargetEntityId = localChild.Entity.Id },
                localChild.Entity.Id,
                cancellationToken);
            children.Add(EnsureMeaningfulTitle(boundChild, localChild.Entity.Title));
        }

        return proposal with { Children = children };
    }

    /// <summary>
    /// Marks structural children the provider advertises as part of its own hierarchy (such as a
    /// season or volume container) rather than leaf media cascaded from a parent identify. These
    /// are preserved when no local entity matches so the user can materialize the missing
    /// structure; leaf children with no local media file are not.
    /// </summary>
    private const string ProviderAdvertisedStructuralMatchReason = "provider-tree";

    private static bool IsProviderAdvertisedStructuralChild(EntityMetadataProposal proposal) =>
        string.Equals(
            proposal.MatchReason,
            ProviderAdvertisedStructuralMatchReason,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSameStructuralChild(StructuralChild localChild, EntityMetadataProposal proposal) {
        if (!IsCompatibleStructuralKind(localChild.Entity.KindCode, proposal.TargetKind)) {
            return false;
        }

        var proposalSortOrder = EntityMetadataPositionRules.SortOrderFor(
            localChild.Entity.KindCode,
            EntityMetadataPositionRules.Normalize(proposal.Patch.Positions));
        if (localChild.SortOrder is { } localSortOrder &&
            proposalSortOrder is { } matchedSortOrder &&
            localSortOrder == matchedSortOrder) {
            return true;
        }

        return !string.IsNullOrWhiteSpace(localChild.Entity.Title) &&
            !string.IsNullOrWhiteSpace(proposal.Patch.Title) &&
            localChild.Entity.Title.Equals(proposal.Patch.Title, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompatibleStructuralKind(string localKind, ProposalKind proposalKind) =>
        localKind == proposalKind.ToEntityKind().ToCode();

    private static EntityMetadataProposal EnsureStructuralPositions(EntityMetadataProposal proposal, StructuralChild child) {
        if (child.SortOrder is not { } sortOrder || proposal.Patch.Positions.Count > 0) {
            return proposal;
        }

        var code = child.Entity.KindCode.Equals(EntityKindRegistry.VideoSeason.Code, StringComparison.OrdinalIgnoreCase)
            ? "seasonNumber"
            : "sortOrder";
        return proposal with {
            Patch = proposal.Patch with {
                Positions = new Dictionary<string, int> { [code] = sortOrder }
            }
        };
    }

    /// <summary>
    /// True when the caller is steering this identify by hand — a manual title search or an
    /// explicit pick-from-candidates request — so the entity's stored ids and urls must not
    /// route the plugin back onto the very match the user is trying to replace.
    /// </summary>
    private static bool ShouldIgnoreExistingIdentityHints(IdentifyQuery? query) =>
        (query?.RequireChoice == true || !string.IsNullOrWhiteSpace(query?.Title)) &&
        string.IsNullOrWhiteSpace(query?.Url) &&
        query?.ExternalIds is not { Count: > 0 };

    private async Task<IReadOnlyList<IdentifyEntitySnapshot>> LoadAncestorSnapshotsAsync(
        EntityRow entity,
        string providerId,
        CancellationToken cancellationToken) {
        var ancestors = new List<IdentifyEntitySnapshot>();
        var parentId = entity.ParentEntityId;
        var visited = new HashSet<Guid> { entity.Id };
        while (parentId is { } id && visited.Add(id)) {
            var parent = await _db.Entities
                .AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
            if (parent is null) {
                break;
            }

            ancestors.Add(await SnapshotAsync(parent, providerId, cancellationToken));
            parentId = parent.ParentEntityId;
        }

        return ancestors;
    }

    /// <summary>
    /// Merges client-supplied parent provider IDs (from a parent proposal not yet applied) into the
    /// immediate ancestor snapshot so a plugin can resolve a child within its parent's context.
    /// </summary>
    private static IReadOnlyList<IdentifyEntitySnapshot> MergeImmediateParentExternalIds(
        IReadOnlyList<IdentifyEntitySnapshot> ancestors,
        IReadOnlyDictionary<string, string>? parentExternalIds) {
        if (parentExternalIds is not { Count: > 0 } || ancestors.Count == 0) {
            return ancestors;
        }

        var immediate = ancestors[0];
        var merged = new Dictionary<string, string>(immediate.ExternalIds ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parentExternalIds) {
            merged[key] = value;
        }

        var updated = new List<IdentifyEntitySnapshot>(ancestors) { [0] = immediate with { ExternalIds = merged } };
        return updated;
    }

    private async Task<IdentifyEntitySnapshot> SnapshotAsync(
        EntityRow entity,
        string providerId,
        CancellationToken cancellationToken,
        EntityKind? kindOverride = null) {
        var hints = await _hints.ResolveAsync(entity.Id, providerId, cancellationToken);
        return new IdentifyEntitySnapshot(
            entity.Id,
            kindOverride ?? entity.KindCode.DecodeAs<EntityKind>(),
            entity.Title,
            hints.ExternalIds,
            hints.Urls);
    }

    private async Task<IdentifyEntitySnapshot> SnapshotFromProposalAsync(
        EntityRow entity,
        string providerId,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        var current = await SnapshotAsync(entity, providerId, cancellationToken);
        var externalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in current.ExternalIds ?? new Dictionary<string, string>()) {
            externalIds[key] = value;
        }

        foreach (var (key, value) in proposal.Patch.ExternalIds) {
            externalIds[key] = value;
        }

        var urls = new List<string>();
        urls.AddRange(current.Urls ?? []);
        urls.AddRange(proposal.Patch.Urls);

        var title = !string.IsNullOrWhiteSpace(proposal.Patch.Title)
            ? proposal.Patch.Title.Trim()
            : current.Title;

        return current with {
            Title = title,
            ExternalIds = externalIds,
            Urls = urls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private async Task<IReadOnlyList<StructuralChild>> LoadStructuralChildrenAsync(Guid parentEntityId, CancellationToken cancellationToken) {
        var children = await _db.Entities
            .AsNoTracking()
            .Where(row => row.ParentEntityId == parentEntityId)
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);

        return children
            .Select(row => new StructuralChild(row.SortOrder, row))
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<string, int>> ResolveStructuralPositionsAsync(
        Guid entityId,
        int? parentSortOrder,
        CancellationToken cancellationToken) {
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (parentSortOrder is { } sortOrder) {
            positions["sortOrder"] = sortOrder;
        }

        var persisted = await _db.EntityPositions
            .AsNoTracking()
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        foreach (var row in persisted) {
            positions[row.Code] = row.Value;
        }

        var seasonNumber = await _db.Entities
            .AsNoTracking()
            .Where(row => row.Id == entityId && row.KindCode == EntityKindRegistry.VideoSeason.Code)
            .Select(row => row.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);
        if (seasonNumber is { } value) {
            positions["seasonNumber"] = value;
        }

        return positions;
    }

    private static bool SupportsKind(PluginManifest manifest, string kind) =>
        manifest.Supports.Any(support => PluginEntityKindCompatibility.SupportsKind(support, kind));

    private sealed record StructuralChild(int? SortOrder, EntityRow Entity);
}
