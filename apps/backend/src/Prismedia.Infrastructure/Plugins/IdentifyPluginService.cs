using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Coordinates provider selection, ID-first match hints, plugin execution, and metadata application.
/// </summary>
public sealed class IdentifyPluginService : IIdentifyProviderService {
    private readonly PrismediaDbContext _db;
    private readonly PluginCatalogService _catalog;
    private readonly IdentifyMatchHintResolver _hints;
    private readonly IdentifyRunnerSelector _runners;
    private readonly EntityMetadataApplyService _apply;

    public IdentifyPluginService(
        PrismediaDbContext db,
        PluginCatalogService catalog,
        IdentifyMatchHintResolver hints,
        IdentifyRunnerSelector runners,
        EntityMetadataApplyService apply) {
        _db = db;
        _catalog = catalog;
        _hints = hints;
        _runners = runners;
        _apply = apply;
    }

    /// <summary>
    /// Lists enabled providers that can identify the requested entity kind.
    /// </summary>
    public async Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(string? entityKind, CancellationToken cancellationToken) {
        var providers = await _catalog.ListProvidersAsync(cancellationToken);
        return providers
            .Where(provider => entityKind is null || provider.Supports.Any(support =>
                PluginEntityKindCompatibility.SupportsKind(support, entityKind)))
            .ToArray();
    }

    /// <summary>
    /// Runs one transient identify lookup for an entity.
    /// </summary>
    public async Task<IdentifyPluginResponse> IdentifyAsync(
        Guid entityId,
        string providerId,
        IdentifyQuery? query,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (hideNsfw && await _db.Entities.AsNoTracking()
                .AnyAsync(entity => entity.Id == entityId && entity.IsNsfw, cancellationToken)) {
            return new IdentifyPluginResponse(false, null, $"Entity '{entityId}' was not found.");
        }

        var entity = await _db.Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == entityId && row.DeletedAt == null, cancellationToken);
        if (entity is null) {
            return new IdentifyPluginResponse(false, null, $"Entity '{entityId}' was not found.");
        }

        var descriptor = await _catalog.FindProviderAsync(providerId, entity.KindCode, cancellationToken);
        if (descriptor is null) {
            return new IdentifyPluginResponse(false, null, $"No compatible provider '{providerId}' supports '{entity.KindCode}'.");
        }

        var providerIsNsfw = await ProviderIsNsfwAsync(descriptor, cancellationToken);
        var auth = await _catalog.GetAuthAsync(descriptor.Manifest, cancellationToken);
        var missingAuth = descriptor.Manifest.Auth
            .Where(field => field.Required && !auth.ContainsKey(field.Key))
            .Select(field => field.Key)
            .ToArray();
        if (missingAuth.Length > 0) {
            return new IdentifyPluginResponse(false, null, $"Missing required plugin credentials: {string.Join(", ", missingAuth)}.");
        }

        var ancestors = await LoadAncestorSnapshotsAsync(entity, descriptor.Manifest.Id, cancellationToken);
        var directResult = await IdentifyEntityWithStructuralContextAsync(
            entity,
            descriptor,
            auth,
            query,
            ancestors,
            parentSortOrder: entity.SortOrder,
            includeNsfw: !hideNsfw,
            visited: [],
            cancellationToken);

        if (directResult.Ok && directResult.Result?.Patch is not null) {
            return MarkNsfwIfNeeded(directResult, providerIsNsfw);
        }

        if (entity.ParentEntityId is not null) {
            var cascadeResult = await CascadeFromParentAsync(entity, descriptor, auth, includeNsfw: !hideNsfw, cancellationToken);
            if (cascadeResult is not null) {
                return MarkNsfwIfNeeded(cascadeResult, providerIsNsfw);
            }
        }

        return MarkNsfwIfNeeded(directResult, providerIsNsfw);
    }

    private async Task<bool> ProviderIsNsfwAsync(PluginDescriptor descriptor, CancellationToken cancellationToken) =>
        descriptor.Manifest.IsNsfw ||
        await _db.ProviderConfigs
            .AsNoTracking()
            .Where(row => row.ProviderCode == descriptor.Manifest.Id && row.Enabled)
            .AnyAsync(row => row.IsNsfw, cancellationToken);

    private static IdentifyPluginResponse MarkNsfwIfNeeded(IdentifyPluginResponse response, bool providerIsNsfw) =>
        providerIsNsfw && response.Ok && response.Result is not null
            ? response with { Result = MarkProposalTreeNsfw(response.Result) }
            : response;

    private static EntityMetadataProposal MarkProposalTreeNsfw(EntityMetadataProposal proposal) {
        if (proposal.Patch is null) {
            return proposal;
        }

        return proposal with {
            Patch = MarkPatchNsfw(proposal.Patch),
            Children = proposal.Children.Select(MarkProposalTreeNsfw).ToArray(),
            Relationships = (proposal.Relationships ?? []).Select(MarkProposalTreeNsfw).ToArray()
        };
    }

    private static EntityMetadataPatch MarkPatchNsfw(EntityMetadataPatch patch) =>
        patch with {
            Flags = patch.Flags is null
                ? new EntityMetadataFlagsPatch(null, true, null)
                : patch.Flags with { IsNsfw = true }
        };

    /// <summary>
    /// Walks up the parent chain looking for an ancestor the provider can identify,
    /// then extracts the target entity's proposal from the ancestor's structural children.
    /// </summary>
    private async Task<IdentifyPluginResponse?> CascadeFromParentAsync(
        EntityRow entity,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        bool includeNsfw,
        CancellationToken cancellationToken) {
        var current = entity;
        while (current.ParentEntityId is { } parentId) {
            var parent = await _db.Entities
                .AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == parentId && row.DeletedAt == null, cancellationToken);
            if (parent is null) break;

            if (!SupportsKind(descriptor.Manifest, parent.KindCode)) {
                current = parent;
                continue;
            }

            var parentAncestors = await LoadAncestorSnapshotsAsync(parent, descriptor.Manifest.Id, cancellationToken);
            var parentResult = await IdentifyEntityWithStructuralContextAsync(
                parent, descriptor, auth, query: null, parentAncestors,
                parentSortOrder: parent.SortOrder, includeNsfw, visited: [], cancellationToken);

            if (!parentResult.Ok || parentResult.Result?.Patch is null) {
                current = parent;
                continue;
            }

            var childProposal = FindEntityInProposalTree(entity.Id, parentResult.Result);
            if (childProposal is not null) {
                return new IdentifyPluginResponse(true, childProposal, null);
            }

            break;
        }

        return null;
    }

    private static EntityMetadataProposal? FindEntityInProposalTree(Guid entityId, EntityMetadataProposal proposal) {
        if (proposal.TargetEntityId == entityId) return proposal;
        foreach (var child in proposal.Children ?? []) {
            var found = FindEntityInProposalTree(entityId, child);
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>
    /// Applies selected metadata proposal fields to an entity.
    /// </summary>
    public Task<bool> ApplyAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        IReadOnlyCollection<string> selectedFields,
        IReadOnlyDictionary<string, string?>? selectedImages,
        CancellationToken cancellationToken) =>
        _apply.ApplyAsync(entityId, proposal, selectedFields, selectedImages, cancellationToken);

    /// <summary>
    /// Applies selected metadata proposal fields to an entity while publishing live progress.
    /// </summary>
    public Task<bool> ApplyAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        IReadOnlyCollection<string> selectedFields,
        IReadOnlyDictionary<string, string?>? selectedImages,
        IdentifyApplyProgressReporter? progress,
        CancellationToken cancellationToken) =>
        _apply.ApplyAsync(entityId, proposal, selectedFields, selectedImages, progress, cancellationToken);

    private static string ResolveAction(
        PluginManifest manifest,
        string entityKind,
        IdentifyQuery? query,
        IdentifyMatchHints hints) {
        var supports = PluginEntityKindCompatibility.ActionsFor(manifest, entityKind)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasQueryTitle = !string.IsNullOrWhiteSpace(query?.Title);
        var hasQueryId = query?.ExternalIds?.ContainsKey(manifest.Id) == true;
        var hasQueryUrl = !string.IsNullOrWhiteSpace(query?.Url);

        if (hasQueryTitle && !hasQueryId && !hasQueryUrl && supports.Contains("search")) {
            return "search";
        }

        var hasExplicitId = query?.ExternalIds?.ContainsKey(manifest.Id) == true ||
            hints.ExternalIds.ContainsKey(manifest.Id);

        if (hasExplicitId && supports.Contains("lookup-id")) {
            return "lookup-id";
        }

        if ((!string.IsNullOrWhiteSpace(query?.Url) || hints.Urls.Count > 0) && supports.Contains("lookup-url")) {
            return "lookup-url";
        }

        return supports.Contains("search") ? "search" : supports.FirstOrDefault() ?? "search";
    }

    private async Task<IdentifyPluginResponse> IdentifyEntityWithStructuralContextAsync(
        EntityRow entity,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        IdentifyQuery? query,
        IReadOnlyList<IdentifyEntitySnapshot> ancestors,
        int? parentSortOrder,
        bool includeNsfw,
        HashSet<Guid> visited,
        CancellationToken cancellationToken) {
        if (!visited.Add(entity.Id)) {
            return new IdentifyPluginResponse(false, null, $"Cycle detected while identifying entity '{entity.Id}'.");
        }

        var resolvedHints = await _hints.ResolveAsync(entity.Id, descriptor.Manifest.Id, cancellationToken);
        var hints = ShouldIgnoreExistingIdentityHints(query)
            ? resolvedHints with {
                ExternalIds = new Dictionary<string, string>(),
                Urls = []
            }
            : resolvedHints;
        var positions = await ResolveStructuralPositionsAsync(entity.Id, parentSortOrder, cancellationToken);
        var structuralContext = ancestors.Count > 0 || positions.Count > 0
            ? new IdentifyStructuralContext(ancestors, positions)
            : null;
        var pluginRequestKind = PluginEntityKindCompatibility.RequestKindFor(descriptor.Manifest, entity.KindCode);
        var request = new IdentifyPluginRequest(
            ProtocolVersion: 2,
            Action: ResolveAction(descriptor.Manifest, entity.KindCode, query, hints),
            Auth: auth,
            Entity: await SnapshotAsync(entity, descriptor.Manifest.Id, cancellationToken, pluginRequestKind),
            Query: query ?? new IdentifyQuery(null, null, null),
            Hints: hints,
            StructuralContext: structuralContext,
            IncludeNsfw: includeNsfw);

        var response = await _runners.Resolve(descriptor).IdentifyAsync(descriptor, request, cancellationToken);
        if (!response.Ok || response.Result is null) {
            visited.Remove(entity.Id);
            return response;
        }

        if (response.Result.Patch is null) {
            visited.Remove(entity.Id);
            return response;
        }

        var proposal = await BuildStructuralProposalAsync(
            entity,
            response.Result,
            descriptor,
            auth,
            [await SnapshotFromProposalAsync(entity, descriptor.Manifest.Id, response.Result, cancellationToken), .. ancestors],
            includeNsfw,
            visited,
            cancellationToken);
        visited.Remove(entity.Id);
        return response with { Result = proposal };
    }

    private async Task<EntityMetadataProposal> BuildStructuralProposalAsync(
        EntityRow entity,
        EntityMetadataProposal providerProposal,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        IReadOnlyList<IdentifyEntitySnapshot> ancestorPath,
        bool includeNsfw,
        HashSet<Guid> visited,
        CancellationToken cancellationToken) {
        var existingChildren = await LoadStructuralChildrenAsync(entity.Id, cancellationToken);
        var boundProviderProposal = await BindLocalStructuralTargetsAsync(providerProposal, entity.Id, cancellationToken);
        var providerStructuralChildren = EntityMetadataProposalTraversal.StructuralChildren(boundProviderProposal);
        var structuralChildren = new List<EntityMetadataProposal>();
        foreach (var child in existingChildren) {
            if (!SupportsKind(descriptor.Manifest, child.Entity.KindCode)) {
                continue;
            }

            var providerChild = providerStructuralChildren.FirstOrDefault(proposal => IsSameStructuralChild(child, proposal));
            if (providerChild is not null) {
                structuralChildren.Add(await HydrateMatchedProviderChildAsync(
                    child,
                    providerChild,
                    descriptor,
                    auth,
                    ancestorPath,
                    includeNsfw,
                    visited,
                    cancellationToken));
                continue;
            }

            var childResponse = await IdentifyEntityWithStructuralContextAsync(
                child.Entity,
                descriptor,
                auth,
                query: null,
                ancestors: ancestorPath,
                parentSortOrder: child.SortOrder,
                includeNsfw,
                visited,
                cancellationToken);
            if (childResponse.Ok && childResponse.Result?.Patch is not null) {
                structuralChildren.Add(EnsureStructuralPositions(childResponse.Result, child));
            }
        }

        return boundProviderProposal with {
            TargetKind = entity.KindCode,
            TargetEntityId = entity.Id,
            Children = MergeStructuralChildren(boundProviderProposal.Children, structuralChildren),
            Relationships = EntityMetadataProposalTraversal.Relationships(boundProviderProposal)
        };
    }

    private async Task<EntityMetadataProposal> HydrateMatchedProviderChildAsync(
        StructuralChild child,
        EntityMetadataProposal providerChild,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        IReadOnlyList<IdentifyEntitySnapshot> ancestorPath,
        bool includeNsfw,
        HashSet<Guid> visited,
        CancellationToken cancellationToken) {
        var providerStructuralChildren = EntityMetadataProposalTraversal.StructuralChildren(providerChild);
        if (!await HasSupportedStructuralChildrenAsync(child.Entity.Id, descriptor.Manifest, cancellationToken)) {
            return providerChild;
        }

        if (providerStructuralChildren.Count > 0 &&
            !await HasMissingSupportedStructuralChildrenAsync(
                child.Entity.Id,
                providerStructuralChildren,
                descriptor.Manifest,
                cancellationToken)) {
            return providerChild;
        }

        var childResponse = await IdentifyEntityWithStructuralContextAsync(
            child.Entity,
            descriptor,
            auth,
            query: null,
            ancestors: ancestorPath,
            parentSortOrder: child.SortOrder,
            includeNsfw,
            visited,
            cancellationToken);
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
            .Where(row => row.ParentEntityId == parentEntityId && row.DeletedAt == null)
            .Select(row => row.KindCode)
            .ToArrayAsync(cancellationToken);
        return childKinds.Any(kind => manifest.Supports.Any(support => IsCompatibleStructuralKind(kind, support.EntityKind)));
    }

    private async Task<bool> HasMissingSupportedStructuralChildrenAsync(
        Guid parentEntityId,
        IReadOnlyList<EntityMetadataProposal> providerChildren,
        PluginManifest manifest,
        CancellationToken cancellationToken) {
        var localChildren = await LoadStructuralChildrenAsync(parentEntityId, cancellationToken);
        return localChildren
            .Where(child => manifest.Supports.Any(support => IsCompatibleStructuralKind(child.Entity.KindCode, support.EntityKind)))
            .Any(child => !providerChildren.Any(providerChild =>
                providerChild.TargetEntityId == child.Entity.Id ||
                IsSameStructuralChild(child, providerChild)));
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

    private static IReadOnlyList<EntityMetadataProposal> MergeStructuralChildren(
        IReadOnlyList<EntityMetadataProposal> providerChildren,
        IReadOnlyList<EntityMetadataProposal> localChildren) {
        if (providerChildren.Count == 0) {
            return localChildren;
        }

        if (localChildren.Count == 0) {
            return providerChildren;
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

    private static int? StructuralSortOrder(EntityMetadataProposal child) {
        var kind = child.TargetKind.Equals("video-episode", StringComparison.OrdinalIgnoreCase)
            ? EntityKindRegistry.Video.Code
            : child.TargetKind;
        return EntityMetadataPositionRules.SortOrderFor(
            kind,
            EntityMetadataPositionRules.Normalize(child.Patch.Positions));
    }

    private static string StructuralKindKey(string kind) =>
        kind.Equals("video-episode", StringComparison.OrdinalIgnoreCase)
            ? EntityKindRegistry.Video.Code
            : kind.Trim().ToLowerInvariant();

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

    private static bool AreCompatibleProposalKinds(string leftKind, string rightKind) =>
        leftKind.Equals(rightKind, StringComparison.OrdinalIgnoreCase) ||
        leftKind.Equals(EntityKindRegistry.Video.Code, StringComparison.OrdinalIgnoreCase) &&
        rightKind.Equals("video-episode", StringComparison.OrdinalIgnoreCase) ||
        leftKind.Equals("video-episode", StringComparison.OrdinalIgnoreCase) &&
        rightKind.Equals(EntityKindRegistry.Video.Code, StringComparison.OrdinalIgnoreCase);

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
