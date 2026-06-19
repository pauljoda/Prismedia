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
        IReadOnlyDictionary<string, string>? parentExternalIds,
        bool hideNsfw,
        CancellationToken cancellationToken,
        bool cascadeChildren = true,
        IIdentifyCascadeSink? sink = null) {
        if (hideNsfw && await _db.Entities.AsNoTracking()
                .AnyAsync(entity => entity.Id == entityId && entity.IsNsfw, cancellationToken)) {
            return new IdentifyPluginResponse(false, null, $"Entity '{entityId}' was not found.");
        }

        var entity = await _db.Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
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

        var ancestors = MergeImmediateParentExternalIds(
            await LoadAncestorSnapshotsAsync(entity, descriptor.Manifest.Id, cancellationToken),
            parentExternalIds);
        var directResult = await IdentifyEntityWithStructuralContextAsync(
            entity,
            descriptor,
            auth,
            query,
            ancestors,
            parentSortOrder: entity.SortOrder,
            includeNsfw: !hideNsfw,
            visited: [],
            cancellationToken,
            cascadeChildren,
            sink);

        if (directResult.Ok && directResult.Result?.Patch is not null) {
            return ApplyNsfwPolicies(directResult, providerIsNsfw);
        }

        // A user who explicitly asked to choose from candidates gets their search results back
        // untouched — the parent's stored-id auto match would discard them and push the user
        // straight through the very match they are trying to replace.
        if (entity.ParentEntityId is not null && query?.RequireChoice != true) {
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
    /// Walks up the parent chain looking for an ancestor the provider can identify, then extracts the
    /// target entity from the ancestor catalog returned by that single provider call. This deliberately
    /// does not run the full structural cascade for the parent: a child search may use parent context as
    /// a bounded fallback, but it must not resolve every sibling before returning to the UI.
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
                .FirstOrDefaultAsync(row => row.Id == parentId, cancellationToken);
            if (parent is null) break;

            if (!SupportsKind(descriptor.Manifest, parent.KindCode)) {
                current = parent;
                continue;
            }

            var parentResult = await IdentifyParentCatalogAsync(parent, descriptor, auth, includeNsfw, cancellationToken);
            if (!parentResult.Ok || parentResult.Result?.Patch is null) {
                current = parent;
                continue;
            }

            var boundCatalog = await BindLocalStructuralTargetsAsync(parentResult.Result, parent.Id, cancellationToken);
            var childProposal = FindEntityInProposalTree(entity.Id, boundCatalog);
            if (childProposal is not null) {
                return new IdentifyPluginResponse(true, childProposal, null);
            }

            break;
        }

        return null;
    }

    private async Task<IdentifyPluginResponse> IdentifyParentCatalogAsync(
        EntityRow parent,
        PluginDescriptor descriptor,
        IReadOnlyDictionary<string, string> auth,
        bool includeNsfw,
        CancellationToken cancellationToken) {
        var resolvedHints = await _hints.ResolveAsync(parent.Id, descriptor.Manifest.Id, cancellationToken);
        var ancestors = await LoadAncestorSnapshotsAsync(parent, descriptor.Manifest.Id, cancellationToken);
        var positions = await ResolveStructuralPositionsAsync(parent.Id, parent.SortOrder, cancellationToken);
        var structuralContext = ancestors.Count > 0 || positions.Count > 0
            ? new IdentifyStructuralContext(ancestors, positions)
            : null;
        var pluginRequestKind = PluginEntityKindCompatibility.RequestKindFor(descriptor.Manifest, parent.KindCode);
        var query = new IdentifyQuery(null, null, null);
        var resolvedAction = ResolveAction(descriptor.Manifest, parent.KindCode, query, resolvedHints);
        var request = new IdentifyPluginRequest(
            ProtocolVersion: 2,
            Action: resolvedAction,
            Auth: auth,
            Entity: await SnapshotAsync(parent, descriptor.Manifest.Id, cancellationToken, pluginRequestKind),
            Query: query,
            Hints: resolvedHints,
            StructuralContext: structuralContext,
            IncludeNsfw: includeNsfw);

        var response = await _runners.Resolve(descriptor).IdentifyAsync(descriptor, request, cancellationToken);
        return await FallBackToSearchAsync(descriptor, parent, request, resolvedAction, response, cancellationToken);
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
        CancellationToken cancellationToken,
        IIdentifyApplyProgressReporter? progress = null) =>
        _apply.ApplyAsync(entityId, proposal, selectedFields, selectedImages, progress, cancellationToken);

    /// <summary>
    /// Applies selected metadata proposal fields to an entity while publishing live progress.
    /// </summary>
    public Task<bool> ApplyAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        IReadOnlyCollection<string> selectedFields,
        IReadOnlyDictionary<string, string?>? selectedImages,
        IIdentifyApplyProgressReporter? progress,
        CancellationToken cancellationToken) =>
        _apply.ApplyAsync(entityId, proposal, selectedFields, selectedImages, progress, cancellationToken);

    private static IdentifyAction ResolveAction(
        PluginManifest manifest,
        string entityKind,
        IdentifyQuery? query,
        IdentifyMatchHints hints) {
        // The manifest declares its supported actions as wire-code strings; compare against the
        // IdentifyAction codes rather than retyping the literals.
        var supports = PluginEntityKindCompatibility.ActionsFor(manifest, entityKind)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Supports(IdentifyAction action) => supports.Contains(action.ToCode());

        // A user asking to pick from candidates forces search mode, so a stored id/url does not route
        // this into a confident lookup whose proposal would only be downgraded back to a candidate.
        if (query?.RequireChoice == true && Supports(IdentifyAction.Search)) {
            return IdentifyAction.Search;
        }

        var hasQueryTitle = !string.IsNullOrWhiteSpace(query?.Title);
        var hasQueryId = query?.ExternalIds?.ContainsKey(manifest.Id) == true;
        var hasQueryUrl = !string.IsNullOrWhiteSpace(query?.Url);

        if (hasQueryTitle && !hasQueryId && !hasQueryUrl && Supports(IdentifyAction.Search)) {
            return IdentifyAction.Search;
        }

        var hasExplicitId = query?.ExternalIds?.ContainsKey(manifest.Id) == true ||
            hints.ExternalIds.ContainsKey(manifest.Id);

        if (hasExplicitId && Supports(IdentifyAction.LookupId)) {
            return IdentifyAction.LookupId;
        }

        if ((!string.IsNullOrWhiteSpace(query?.Url) || hints.Urls.Count > 0) && Supports(IdentifyAction.LookupUrl)) {
            return IdentifyAction.LookupUrl;
        }

        if (Supports(IdentifyAction.Search)) {
            return IdentifyAction.Search;
        }

        // No search support: fall back to the first declared action that maps to a known IdentifyAction.
        foreach (var action in supports) {
            if (action.TryDecodeAs<IdentifyAction>(out var decoded)) {
                return decoded;
            }
        }

        return IdentifyAction.Search;
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
        CancellationToken cancellationToken,
        bool cascadeChildren = true,
        IIdentifyCascadeSink? sink = null,
        bool streamRootProgress = true) {
        if (!visited.Add(entity.Id)) {
            return new IdentifyPluginResponse(false, null, $"Cycle detected while identifying entity '{entity.Id}'.");
        }

        var resolvedHints = await _hints.ResolveAsync(entity.Id, descriptor.Manifest.Id, cancellationToken);
        var ignoreStoredIdentity = ShouldIgnoreExistingIdentityHints(query);
        var hints = ignoreStoredIdentity
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
        var resolvedAction = ResolveAction(descriptor.Manifest, entity.KindCode, query, hints);
        var entitySnapshot = await SnapshotAsync(entity, descriptor.Manifest.Id, cancellationToken, pluginRequestKind);
        if (ignoreStoredIdentity) {
            // Plugins read stored ids from the entity snapshot as well as the hints; a manual
            // search must hide both or the plugin re-locks onto the match being replaced.
            entitySnapshot = entitySnapshot with { ExternalIds = new Dictionary<string, string>(), Urls = [] };
        }

        var request = new IdentifyPluginRequest(
            ProtocolVersion: 2,
            Action: resolvedAction,
            Auth: auth,
            Entity: entitySnapshot,
            Query: query ?? new IdentifyQuery(null, null, null),
            Hints: hints,
            StructuralContext: structuralContext,
            IncludeNsfw: includeNsfw);

        var response = await _runners.Resolve(descriptor).IdentifyAsync(descriptor, request, cancellationToken);
        response = await FallBackToSearchAsync(descriptor, entity, request, resolvedAction, response, cancellationToken);
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
            cancellationToken,
            cascadeChildren,
            sink,
            streamRootProgress);
        visited.Remove(entity.Id);
        return response with { Result = proposal };
    }

    /// <summary>
    /// Recovers a re-identify that a stored id or url routed down the lookup path. Once any provider id
    /// or url is persisted on an entity, <see cref="ResolveAction"/> locks it into lookup-id/lookup-url,
    /// so a provider whose id lookup is flakier than its search — or that never implemented id lookup for
    /// this kind — can no longer re-find an entity it originally matched by name. When the lookup path
    /// returns no usable patch, retry once as a clean search, stripping the stored ids and urls from both
    /// the hints and the entity snapshot so the request runs exactly as the first identify did before
    /// anything was stored. The original failing response is preserved if the search also finds nothing.
    /// </summary>
    private async Task<IdentifyPluginResponse> FallBackToSearchAsync(
        PluginDescriptor descriptor,
        EntityRow entity,
        IdentifyPluginRequest request,
        IdentifyAction resolvedAction,
        IdentifyPluginResponse response,
        CancellationToken cancellationToken) {
        var lookupRouted = resolvedAction is IdentifyAction.LookupId or IdentifyAction.LookupUrl;
        if (!lookupRouted || (response.Ok && response.Result?.Patch is not null)) {
            return response;
        }

        // Explicit user-picked provider IDs, including candidate picks from the identify queue,
        // must not silently turn into a generic provider search if that exact lookup misses.
        if (request.Query.ExternalIds?.ContainsKey(descriptor.Manifest.Id) == true) {
            return response;
        }

        var supportsSearch = PluginEntityKindCompatibility
            .ActionsFor(descriptor.Manifest, entity.KindCode)
            .Any(action => action.Equals(IdentifyAction.Search.ToCode(), StringComparison.OrdinalIgnoreCase));
        if (!supportsSearch) {
            return response;
        }

        var searchRequest = request with {
            Action = IdentifyAction.Search,
            Hints = request.Hints with { ExternalIds = new Dictionary<string, string>(), Urls = [] },
            Entity = request.Entity with { ExternalIds = new Dictionary<string, string>(), Urls = [] }
        };
        var searchResponse = await _runners.Resolve(descriptor).IdentifyAsync(descriptor, searchRequest, cancellationToken);
        return searchResponse.Ok && searchResponse.Result?.Patch is not null ? searchResponse : response;
    }

}
