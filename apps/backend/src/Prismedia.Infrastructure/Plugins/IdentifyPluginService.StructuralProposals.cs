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
        CancellationToken cancellationToken) {
        // Bind the children the provider returned in its own proposal to local entities. We do NOT
        // recurse into a separate provider call per local child here: child identification is driven
        // incrementally by the client (one POST /entities/{childId} at a time), so a parent identify
        // is a single provider request that returns just the parent plus the provider-advertised tree.
        var boundProviderProposal = await BindLocalStructuralTargetsAsync(providerProposal, entity.Id, cancellationToken);

        return boundProviderProposal with {
            TargetKind = entity.KindCode,
            TargetEntityId = entity.Id,
            Children = boundProviderProposal.Children,
            Relationships = EntityMetadataProposalTraversal.Relationships(boundProviderProposal)
        };
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
            children.Add(boundChild);
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

    private static bool IsCompatibleStructuralKind(string localKind, string proposalKind) =>
        localKind.Equals(proposalKind, StringComparison.OrdinalIgnoreCase) ||
        localKind.Equals(EntityKindRegistry.Video.Code, StringComparison.OrdinalIgnoreCase) &&
        proposalKind.Equals("video-episode", StringComparison.OrdinalIgnoreCase);

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

    private static bool ShouldIgnoreExistingIdentityHints(IdentifyQuery? query) =>
        !string.IsNullOrWhiteSpace(query?.Title) &&
        string.IsNullOrWhiteSpace(query.Url) &&
        query.ExternalIds is not { Count: > 0 };

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
                .FirstOrDefaultAsync(row => row.Id == id && row.DeletedAt == null, cancellationToken);
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
        string? kindOverride = null) {
        var hints = await _hints.ResolveAsync(entity.Id, providerId, cancellationToken);
        return new IdentifyEntitySnapshot(
            entity.Id,
            kindOverride ?? entity.KindCode,
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
            .Where(row => row.ParentEntityId == parentEntityId && row.DeletedAt == null)
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
