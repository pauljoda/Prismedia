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
    private readonly DotnetPluginProcessRunner _runner;
    private readonly EntityMetadataApplyService _apply;

    public IdentifyPluginService(
        PrismediaDbContext db,
        PluginCatalogService catalog,
        IdentifyMatchHintResolver hints,
        DotnetPluginProcessRunner runner,
        EntityMetadataApplyService apply) {
        _db = db;
        _catalog = catalog;
        _hints = hints;
        _runner = runner;
        _apply = apply;
    }

    /// <summary>
    /// Lists enabled providers that can identify the requested entity kind.
    /// </summary>
    public async Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(string? entityKind, CancellationToken cancellationToken) {
        var providers = await _catalog.ListProvidersAsync(cancellationToken);
        return providers
            .Where(provider => entityKind is null || provider.Supports.Any(support =>
                support.EntityKind.Equals(entityKind, StringComparison.OrdinalIgnoreCase)))
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
            visited: [],
            cancellationToken);

        if (directResult.Ok && directResult.Result?.Patch is not null) {
            return MarkNsfwIfNeeded(directResult, providerIsNsfw);
        }

        if (entity.ParentEntityId is not null) {
            var cascadeResult = await CascadeFromParentAsync(entity, descriptor, auth, cancellationToken);
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
                parentSortOrder: parent.SortOrder, visited: [], cancellationToken);

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

    private static string ResolveAction(
        PluginManifest manifest,
        string entityKind,
        IdentifyQuery? query,
        IdentifyMatchHints hints) {
        var supports = manifest.Supports
            .Where(support => support.EntityKind.Equals(entityKind, StringComparison.OrdinalIgnoreCase))
            .SelectMany(support => support.Actions)
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
        var request = new IdentifyPluginRequest(
            ProtocolVersion: 2,
            Action: ResolveAction(descriptor.Manifest, entity.KindCode, query, hints),
            Auth: auth,
            Entity: await SnapshotAsync(entity, descriptor.Manifest.Id, cancellationToken),
            Query: query ?? new IdentifyQuery(null, null, null),
            Hints: hints,
            StructuralContext: structuralContext);

        var response = await _runner.IdentifyAsync(descriptor, request, cancellationToken);
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
        HashSet<Guid> visited,
        CancellationToken cancellationToken) {
        var existingChildren = await LoadStructuralChildrenAsync(entity.Id, cancellationToken);
        var structuralChildren = new List<EntityMetadataProposal>();
        foreach (var child in existingChildren) {
            if (!SupportsKind(descriptor.Manifest, child.Entity.KindCode)) {
                continue;
            }

            var childResponse = await IdentifyEntityWithStructuralContextAsync(
                child.Entity,
                descriptor,
                auth,
                query: null,
                ancestors: ancestorPath,
                parentSortOrder: child.SortOrder,
                visited,
                cancellationToken);
            if (childResponse.Ok && childResponse.Result?.Patch is not null) {
                structuralChildren.Add(childResponse.Result);
            }
        }

        return providerProposal with {
            TargetKind = entity.KindCode,
            TargetEntityId = entity.Id,
            Children = structuralChildren,
            Relationships = RelationshipProposals(providerProposal)
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
        CancellationToken cancellationToken) {
        var hints = await _hints.ResolveAsync(entity.Id, providerId, cancellationToken);
        return new IdentifyEntitySnapshot(
            entity.Id,
            entity.KindCode,
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
        manifest.Supports.Any(support => support.EntityKind.Equals(kind, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<EntityMetadataProposal> RelationshipProposals(EntityMetadataProposal proposal) {
        var relationships = new List<EntityMetadataProposal>();
        if (proposal.Relationships is { Count: > 0 }) {
            relationships.AddRange(proposal.Relationships);
        }

        relationships.AddRange((proposal.Children ?? []).Where(child => IsRelationshipMetadataKind(child.TargetKind)));

        return relationships
            .GroupBy(child => child.ProposalId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool IsRelationshipMetadataKind(string kind) =>
        kind is "person" or "studio" or "tag";

    private sealed record StructuralChild(int? SortOrder, EntityRow Entity);
}
