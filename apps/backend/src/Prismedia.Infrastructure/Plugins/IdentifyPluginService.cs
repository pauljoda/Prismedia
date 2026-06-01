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
public sealed partial class IdentifyPluginService : IIdentifyProviderService {
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
            return ApplyNsfwPolicies(directResult, providerIsNsfw);
        }

        if (entity.ParentEntityId is not null) {
            var cascadeResult = await CascadeFromParentAsync(entity, descriptor, auth, includeNsfw: !hideNsfw, cancellationToken);
            if (cascadeResult is not null) {
                return ApplyNsfwPolicies(cascadeResult, providerIsNsfw);
            }
        }

        return ApplyNsfwPolicies(directResult, providerIsNsfw);
    }

    private async Task<bool> ProviderIsNsfwAsync(PluginDescriptor descriptor, CancellationToken cancellationToken) =>
        descriptor.Manifest.IsNsfw ||
        await _db.ProviderConfigs
            .AsNoTracking()
            .Where(row => row.ProviderCode == descriptor.Manifest.Id && row.Enabled)
            .AnyAsync(row => row.IsNsfw, cancellationToken);

    /// <summary>
    /// Applies NSFW marking to a proposal tree. Two independent rules contribute: an NSFW provider
    /// (such as a Stash community scraper) marks its entire proposal tree, and any node whose
    /// provider-supplied classification reads as mature (R, 18+, pornographic, and so on) marks
    /// that node and everything beneath it — even when the provider itself is not NSFW.
    /// </summary>
    private static IdentifyPluginResponse ApplyNsfwPolicies(IdentifyPluginResponse response, bool providerIsNsfw) {
        if (!response.Ok || response.Result is null) {
            return response;
        }

        var result = providerIsNsfw ? MarkProposalTreeNsfw(response.Result) : response.Result;
        result = MarkMatureClassificationNsfw(result);
        return response with { Result = result };
    }

    private static EntityMetadataProposal MarkMatureClassificationNsfw(EntityMetadataProposal proposal) {
        if (proposal.Patch is null) {
            return proposal;
        }

        if (ContentClassificationNsfwPolicy.IsMature(proposal.Patch.Classification)) {
            return MarkProposalTreeNsfw(proposal);
        }

        return proposal with {
            Children = proposal.Children.Select(MarkMatureClassificationNsfw).ToArray(),
            Relationships = (proposal.Relationships ?? []).Select(MarkMatureClassificationNsfw).ToArray()
        };
    }

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

}
