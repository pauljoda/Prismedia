using System.Globalization;
using Prismedia.Application.Requests;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Requests;

/// <summary>
/// Runs request-time searches and lookups against enabled plugin metadata providers for every
/// requestable kind, with no library entity involved. Behavior is driven entirely by
/// <see cref="RequestKindDescriptor"/>s: the descriptor says which plugin kind to query and how
/// children behave, so adding a media kind never adds another source class. Identity-only background
/// reads use the central router; interactive review keeps the exact plugin selected during search.
/// </summary>
public sealed class PluginRequestMetadataSource(
    PluginCatalogService catalog,
    IPluginIdentityRouter identityRouter,
    IdentifyRunnerSelector runners)
    : IRequestMetadataSearchSource, IPluginRequestSearchSource, IRequestMetadataEnricher, IPluginRequestDetailSource,
      IPluginRequestReviewSource, IPluginRequestProposalSource {
    private static readonly string SearchAction = IdentifyAction.Search.ToCode();
    private static readonly string LookupIdAction = IdentifyAction.LookupId.ToCode();

    // Resolved proposals are stable on the minutes scale, but every surface that reads one — request
    // detail pages, the series page's Season Pass options, a commit right after review, container
    // sync — used to pay a fresh plugin round-trip each time. A short process-wide TTL cache makes
    // repeat reads instant without holding provider data long enough to go stale. The source is
    // scoped, hence the static cache; the cap bounds memory (proposals with children can be large).
    private static readonly TimeSpan ProposalTtl = TimeSpan.FromMinutes(15);
    private const int ProposalCacheCap = 128;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ProposalCacheKey, (DateTimeOffset At, EntityMetadataProposal Proposal)> ProposalCache = new();

    public async Task<IReadOnlyList<RequestSearchResult>> SearchAsync(
        RequestKindDescriptor descriptor, string query, bool hideNsfw, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(query)) {
            return [];
        }

        // A committable container (an author, an artist) only makes sense from a provider that can also
        // resolve its children by id — otherwise the per-child acquisition fan-out can't resolve anything.
        var child = RequestKindRegistry.ChildOf(descriptor);
        var requiredChildLookupKind = descriptor.IsContainer && child is { Committable: true } ? child.PluginKindCode : null;

        var providers = (await catalog.ListInstalledProvidersAsync(cancellationToken))
            .Where(provider => provider.Enabled && (!provider.IsNsfw || !hideNsfw))
            .Where(provider => provider.Supports.Any(support =>
                PluginEntityKindCompatibility.SupportsKind(support, descriptor.PluginKindCode) && support.Actions.Contains(SearchAction)))
            // Every result must be reviewable: its detail page resolves the item by id, so a provider
            // that can search a kind but not look it up would only produce dead-end results.
            .Where(provider => provider.Supports.Any(support =>
                PluginEntityKindCompatibility.SupportsKind(support, descriptor.PluginKindCode) && support.Actions.Contains(LookupIdAction)))
            .Where(provider => requiredChildLookupKind is null || provider.Supports.Any(support =>
                PluginEntityKindCompatibility.SupportsKind(support, requiredChildLookupKind) && support.Actions.Contains(LookupIdAction)))
            .ToArray();
        if (providers.Length == 0) {
            return [];
        }

        var results = new List<RequestSearchResult>();
        foreach (var provider in providers) {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptorForProvider = await catalog.FindProviderAsync(provider.Id, descriptor.PluginKindCode, cancellationToken);
            if (descriptorForProvider is null) {
                continue;
            }

            results.AddRange(await RunSearchAsync(
                descriptor,
                provider,
                descriptorForProvider,
                query,
                fields: null,
                hideNsfw,
                cancellationToken));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RequestSearchResult>> SearchAsync(
        RequestKindDescriptor descriptor,
        string pluginId,
        IReadOnlyDictionary<string, string> fields,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var selected = await SelectSearchProviderAsync(descriptor, pluginId, hideNsfw, cancellationToken);
        var validated = ValidateSearchFields(selected.Provider, selected.Schema, fields);
        return await RunSearchAsync(
            descriptor,
            selected.Provider,
            selected.Descriptor,
            validated.CompatibilityTitle,
            validated.Fields,
            hideNsfw,
            cancellationToken);
    }

    private async Task<IReadOnlyList<RequestSearchResult>> RunSearchAsync(
        RequestKindDescriptor requestKind,
        PluginProvider provider,
        PluginDescriptor descriptor,
        string? compatibilityTitle,
        IReadOnlyDictionary<string, string>? fields,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var auth = await catalog.GetAuthAsync(descriptor.Manifest, cancellationToken);
        var request = new IdentifyPluginRequest(
            ProtocolVersion: PluginProtocol.CurrentVersion,
            Action: IdentifyAction.Search,
            Auth: auth,
            Entity: new IdentifyEntitySnapshot(
                Guid.NewGuid(),
                requestKind.PluginEntityKind,
                compatibilityTitle ?? string.Empty,
                new Dictionary<string, string>(),
                []),
            Query: new IdentifyQuery(compatibilityTitle, null, null, Fields: fields),
            Hints: new IdentifyMatchHints(new Dictionary<string, string>(), [], compatibilityTitle, null),
            StructuralContext: null,
            IncludeNsfw: !hideNsfw,
            IncludeRelationshipDetails: false,
            IncludeStructuralChildren: false);

        var response = await runners.Resolve(descriptor).IdentifyAsync(descriptor, request, cancellationToken);
        return (response.Result?.Candidates ?? [])
            .Select(candidate => MapCandidate(provider, candidate, requestKind))
            .Where(result => result is not null)
            .Select(result => result!)
            .ToArray();
    }

    private async Task<SelectedSearchProvider> SelectSearchProviderAsync(
        RequestKindDescriptor descriptor,
        string pluginId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var provider = (await catalog.ListInstalledProvidersAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        if (provider is null || !provider.Enabled || hideNsfw && provider.IsNsfw) {
            throw new RequestSearchValidationException($"Plugin '{pluginId}' is not enabled for this search.");
        }
        if (provider.MissingAuthKeys.Count > 0) {
            throw new RequestSearchValidationException(
                $"Plugin '{provider.Id}' is missing required authentication: {string.Join(", ", provider.MissingAuthKeys)}.");
        }

        var supports = provider.Supports
            .Where(support => PluginEntityKindCompatibility.SupportsKind(support, descriptor.PluginKindCode))
            .ToArray();
        var searchSupport = supports.FirstOrDefault(support =>
            support.Actions.Contains(SearchAction, StringComparer.OrdinalIgnoreCase));
        var supportsLookup = supports.Any(support =>
            support.Actions.Contains(LookupIdAction, StringComparer.OrdinalIgnoreCase));
        if (searchSupport?.Search is null || !supportsLookup) {
            throw new RequestSearchValidationException(
                $"Plugin '{provider.Id}' does not declare Search and LookupId support for '{descriptor.Kind.ToCode()}'.");
        }

        var pluginDescriptor = await catalog.FindProviderAsync(provider.Id, descriptor.PluginKindCode, cancellationToken);
        if (pluginDescriptor is null) {
            throw new RequestSearchValidationException($"Plugin '{provider.Id}' is not available for this search.");
        }

        return new SelectedSearchProvider(provider, pluginDescriptor, searchSupport.Search);
    }

    private static ValidatedSearchFields ValidateSearchFields(
        PluginProvider provider,
        PluginSearchDefinition schema,
        IReadOnlyDictionary<string, string> fields) {
        if (fields is null) {
            throw new RequestSearchValidationException("Plugin search fields are required.");
        }

        var definitions = schema.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in fields) {
            if (!definitions.TryGetValue(key, out var definition)) {
                throw new RequestSearchValidationException(
                    $"Field '{key}' is not declared by plugin '{provider.Id}'.");
            }

            var normalizedValue = value?.Trim() ?? string.Empty;
            ValidateSearchFieldValue(provider, definition, normalizedValue);
            normalized[key] = normalizedValue;
        }

        var missing = schema.Fields.FirstOrDefault(field =>
            field.Required && (!normalized.TryGetValue(field.Key, out var value) || value.Length == 0));
        if (missing is not null) {
            throw new RequestSearchValidationException(
                $"Field '{missing.Key}' is required by plugin '{provider.Id}'.");
        }

        var compatibilityTitle = schema.Fields
            .Where(field => field.Type == PluginSearchFieldType.Text)
            .Select(field => normalized.GetValueOrDefault(field.Key))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return new ValidatedSearchFields(normalized, compatibilityTitle);
    }

    private static void ValidateSearchFieldValue(
        PluginProvider provider,
        PluginSearchField field,
        string value) {
        if (value.Length == 0 || field.Type == PluginSearchFieldType.Text) {
            return;
        }

        var valid = field.Type switch {
            PluginSearchFieldType.Number => decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _),
            PluginSearchFieldType.Year => value.Length == 4
                && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                && year is >= 1000 and <= 9999,
            _ => false
        };
        if (!valid) {
            throw new RequestSearchValidationException(
                $"Field '{field.Key}' is not a valid {field.Type.ToCode()} value for plugin '{provider.Id}'.");
        }
    }

    public async Task<RequestMetadataEnrichment?> LookupByIdAsync(
        EntityKind kind, ExternalIdentity identity, bool hideNsfw, CancellationToken cancellationToken) {
        var proposal = await ResolveProposalCoreAsync(kind, identity, hideNsfw, includeChildren: false, cancellationToken);
        if (proposal?.Patch is not { } patch) {
            return null;
        }

        return new RequestMetadataEnrichment(
            patch.Description,
            RequestProposalReading.BestImage(proposal),
            RequestProposalReading.YearFromDates(patch.Dates));
    }

    public async Task<RequestDetailResponse?> GetDetailAsync(
        RequestKindDescriptor descriptor, string externalId, bool hideNsfw, CancellationToken cancellationToken) {
        var identity = RequestProposalReading.ParseQualifiedIdentity(externalId);
        if (identity is null) {
            return null;
        }

        // Resolve WITH structural children when the kind offers child options, so a container surfaces
        // its works (an author's books, an artist's albums) — and a series-member book its sibling
        // volumes — as toggleable child options the request fans out into acquisitions.
        var proposal = await ResolveProposalAsync(descriptor, identity, hideNsfw,
            includeChildren: descriptor.ChildKind is not null, cancellationToken);
        if (proposal?.Patch is not { } patch) {
            return null;
        }

        var child = RequestKindRegistry.ChildOf(descriptor);
        var children = child is null
            ? []
            : proposal.Children
                .Where(node => !node.TargetKind.IsRelationship())
                .Select(node => MapChild(identity.Namespace, node, child))
                .Where(option => option is not null)
                .Select(option => option!)
                .ToArray();

        var subtitle = descriptor.IsContainer
            ? children.Length > 0 ? $"{children.Length} {ChildNoun(child!, children.Length)}" : null
            : RequestProposalReading.AuthorFromCredits(patch) ?? RequestProposalReading.PrimaryCredit(patch);

        // Surface everything the proposal carries — this is the same data identify would apply to a
        // library entity, so the review page reads like the entity will once it exists.
        var tracks = RequestProposalReading.TracksOf(proposal);
        return new RequestDetailResponse(
            Source: RequestProviderKind.Plugin,
            Kind: descriptor.Kind,
            ExternalId: externalId,
            Title: string.IsNullOrWhiteSpace(patch.Title) ? identity.Value : patch.Title,
            Subtitle: subtitle,
            Year: RequestProposalReading.YearFromDates(patch.Dates),
            Dates: patch.Dates,
            Overview: patch.Description,
            PosterUrl: RequestProposalReading.BestImageOfKind(proposal, EntityFileRole.Poster.ToCode())
                ?? RequestProposalReading.BestImage(proposal),
            BackdropUrl: RequestProposalReading.BestImageOfKind(proposal, EntityFileRole.Backdrop.ToCode()),
            Rating: null,
            RuntimeMinutes: RequestProposalReading.RuntimeMinutesOf(patch),
            Certification: null,
            TrackCount: tracks.Count > 0 ? tracks.Count : null,
            Tags: patch.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToArray(),
            Studios: string.IsNullOrWhiteSpace(patch.Studio) ? [] : [patch.Studio],
            Credits: [],
            Cast: RequestProposalReading.CastOf(proposal),
            Ratings: [],
            Children: children,
            Tracks: tracks,
            Tracked: false,
            UpstreamId: null,
            Monitored: null,
            ServiceOptions: new RequestServiceOptionsResponse([], [], [], []));
    }

    /// <inheritdoc />
    public async Task<RequestReviewResponse?> ReviewAsync(
        RequestReviewRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        await ResolveReviewAsync(request, hideNsfw, bypassCache: false, cancellationToken);

    /// <inheritdoc />
    public async Task<RequestReviewResponse?> RevalidateAsync(
        RequestReviewRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        await ResolveReviewAsync(request, hideNsfw, bypassCache: true, cancellationToken);

    private async Task<RequestReviewResponse?> ResolveReviewAsync(
        RequestReviewRequest request,
        bool hideNsfw,
        bool bypassCache,
        CancellationToken cancellationToken) {
        var descriptor = RequestKindRegistry.Find(request.Kind);
        if (descriptor is null || string.IsNullOrWhiteSpace(request.PluginId) || request.ExternalIdentity is null) {
            return null;
        }

        var route = new PluginIdentityRoute(request.PluginId, request.ExternalIdentity);
        var provider = await ValidateExplicitRouteAsync(descriptor.PluginEntityKind, route, hideNsfw, cancellationToken);
        if (provider is null) {
            return null;
        }

        var proposal = bypassCache
            ? await RunRouteAsync(
                descriptor.PluginEntityKind,
                route,
                hideNsfw,
                includeChildren: true,
                cancellationToken)
            : await ResolveExplicitProposalAsync(
                descriptor.PluginEntityKind,
                route,
                hideNsfw,
                includeChildren: true,
                cancellationToken);
        if (proposal?.Patch is null
            || !IsCompatibleTarget(descriptor, proposal.TargetKind)
            || !string.Equals(proposal.Provider, provider.Id, StringComparison.OrdinalIgnoreCase)
            || !DeclaresLookupIdentity(provider, proposal.TargetKind.ToEntityKind().ToCode(), route.Identity.Namespace)
            || !string.Equals(
                RequestProposalReading.IdentityValueFor(route.Identity.Namespace, proposal),
                route.Identity.Value,
                StringComparison.Ordinal)
            || !HasUniqueStructuralProposalIds(proposal)) {
            return null;
        }

        var targets = BuildReviewTargets(provider, descriptor, route.Identity, proposal);
        if (!HasUniqueRequestableTargetIdentities(targets)) {
            return null;
        }

        return new RequestReviewResponse(
            PluginId: provider.Id,
            ExternalIdentity: route.Identity,
            EntityKind: proposal.TargetKind.ToEntityKind(),
            Kind: descriptor.Kind,
            Proposal: proposal,
            Revision: RequestProposalRevision.Compute(proposal),
            Targets: targets);
    }

    public Task<EntityMetadataProposal?> ResolveProposalAsync(
        RequestKindDescriptor descriptor, ExternalIdentity identity, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) =>
        ResolveProposalCoreAsync(descriptor.PluginEntityKind, identity, hideNsfw, includeChildren, cancellationToken);

    /// <summary>
    /// Resolves through one explicit plugin route after validating that the installed, enabled plugin
    /// declares LookupId support for both the entity kind and identity namespace. Unlike identity-only
    /// fallback, this never substitutes another plugin that handles the same namespace.
    /// </summary>
    public async Task<EntityMetadataProposal?> ResolveProposalAsync(
        RequestKindDescriptor descriptor,
        PluginIdentityRoute route,
        bool hideNsfw,
        bool includeChildren,
        CancellationToken cancellationToken) =>
        await ValidateExplicitRouteAsync(descriptor.PluginEntityKind, route, hideNsfw, cancellationToken) is null
            ? null
            : await ResolveExplicitProposalAsync(
                descriptor.PluginEntityKind,
                route,
                hideNsfw,
                includeChildren,
                cancellationToken);

    /// <summary>
    /// Routes a persistent identity to every capable LookupId plugin for the given media kind, gating
    /// on the same enabled/NSFW rules as search. Shared-namespace routes are tried deterministically.
    /// </summary>
    private async Task<EntityMetadataProposal?> ResolveProposalCoreAsync(
        EntityKind entityKind, ExternalIdentity identity, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) {
        var cacheKey = new ProposalCacheKey(entityKind, PluginId: null, identity, hideNsfw, includeChildren);
        if (ProposalCache.TryGetValue(cacheKey, out var hit) && DateTimeOffset.UtcNow - hit.At < ProposalTtl) {
            return hit.Proposal;
        }

        var proposal = await ResolveProposalUncachedAsync(entityKind, identity, hideNsfw, includeChildren, cancellationToken);
        // Only successful resolutions are cached: a transient provider failure should retry, and a
        // gated/unknown id is cheap to re-answer.
        if (proposal is not null) {
            EvictForCapacity();
            ProposalCache[cacheKey] = (DateTimeOffset.UtcNow, proposal);
        }

        return proposal;
    }

    private async Task<EntityMetadataProposal?> ResolveExplicitProposalAsync(
        EntityKind entityKind,
        PluginIdentityRoute route,
        bool hideNsfw,
        bool includeChildren,
        CancellationToken cancellationToken) {
        var cacheKey = new ProposalCacheKey(
            entityKind,
            route.PluginId.ToLowerInvariant(),
            route.Identity,
            hideNsfw,
            includeChildren);
        if (ProposalCache.TryGetValue(cacheKey, out var hit) && DateTimeOffset.UtcNow - hit.At < ProposalTtl) {
            return hit.Proposal;
        }

        var proposal = await RunRouteAsync(entityKind, route, hideNsfw, includeChildren, cancellationToken);
        if (proposal is not null) {
            EvictForCapacity();
            ProposalCache[cacheKey] = (DateTimeOffset.UtcNow, proposal);
        }

        return proposal;
    }

    private static void EvictForCapacity() {
        if (ProposalCache.Count < ProposalCacheCap) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var (key, value) in ProposalCache) {
            if (now - value.At >= ProposalTtl) {
                ProposalCache.TryRemove(key, out _);
            }
        }

        while (ProposalCache.Count >= ProposalCacheCap && !ProposalCache.IsEmpty) {
            var oldest = ProposalCache.MinBy(pair => pair.Value.At);
            ProposalCache.TryRemove(oldest.Key, out _);
        }
    }

    private async Task<EntityMetadataProposal?> ResolveProposalUncachedAsync(
        EntityKind entityKind, ExternalIdentity identity, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) {
        var kindCode = entityKind.ToCode();
        var routes = await identityRouter.ResolveAsync(
            kindCode,
            IdentifyAction.LookupId,
            [identity],
            cancellationToken);
        if (routes.Count == 0) {
            return null;
        }

        var providers = await catalog.ListInstalledProvidersAsync(cancellationToken);
        foreach (var route in routes) {
            var provider = providers.FirstOrDefault(candidate =>
                candidate.Id.Equals(route.PluginId, StringComparison.OrdinalIgnoreCase));
            if (provider is null || (hideNsfw && provider.IsNsfw)) {
                continue;
            }

            if (await RunRouteAsync(entityKind, route, hideNsfw, includeChildren, cancellationToken) is { } proposal) {
                return proposal;
            }
        }

        return null;
    }

    private async Task<EntityMetadataProposal?> RunRouteAsync(
        EntityKind entityKind,
        PluginIdentityRoute route,
        bool hideNsfw,
        bool includeChildren,
        CancellationToken cancellationToken) {
        var descriptor = await catalog.FindProviderAsync(route.PluginId, entityKind.ToCode(), cancellationToken);
        if (descriptor is null) {
            return null;
        }

        var ids = new Dictionary<string, string> {
            [route.Identity.Namespace] = route.Identity.Value
        };
        var auth = await catalog.GetAuthAsync(descriptor.Manifest, cancellationToken);
        var request = new IdentifyPluginRequest(
            ProtocolVersion: PluginProtocol.CurrentVersion,
            Action: IdentifyAction.LookupId,
            Auth: auth,
            // No library entity: a synthetic snapshot carrying the known identity is enough for an id lookup.
            Entity: new IdentifyEntitySnapshot(Guid.NewGuid(), entityKind, string.Empty, ids, []),
            Query: new IdentifyQuery(null, null, ids),
            Hints: new IdentifyMatchHints(ids, [], null, null),
            StructuralContext: null,
            IncludeNsfw: !hideNsfw,
            IncludeRelationshipDetails: false,
            IncludeStructuralChildren: includeChildren);

        var response = await runners.Resolve(descriptor).IdentifyAsync(descriptor, request, cancellationToken);
        return response.Result;
    }

    private async Task<PluginProvider?> ValidateExplicitRouteAsync(
        EntityKind entityKind,
        PluginIdentityRoute route,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var provider = (await catalog.ListInstalledProvidersAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id.Equals(route.PluginId, StringComparison.OrdinalIgnoreCase));
        if (provider is null || !provider.Enabled || provider.MissingAuthKeys.Count > 0 || hideNsfw && provider.IsNsfw) {
            return null;
        }

        var kindCode = entityKind.ToCode();
        if (!DeclaresLookupIdentity(provider, kindCode, route.Identity.Namespace)) {
            return null;
        }

        return await catalog.FindProviderAsync(provider.Id, kindCode, cancellationToken) is null
            ? null
            : provider;
    }

    private static IReadOnlyList<RequestReviewTarget> BuildReviewTargets(
        PluginProvider provider,
        RequestKindDescriptor rootDescriptor,
        ExternalIdentity rootIdentity,
        EntityMetadataProposal proposal) {
        var targets = new List<RequestReviewTarget>();
        AddTarget(proposal, rootDescriptor, rootIdentity, targets);
        AddChildTargets(provider, proposal, rootDescriptor, targets);
        return targets;
    }

    private static bool HasUniqueStructuralProposalIds(EntityMetadataProposal proposal) {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return Visit(proposal);

        bool Visit(EntityMetadataProposal node) {
            if (string.IsNullOrWhiteSpace(node.ProposalId) || !ids.Add(node.ProposalId)) {
                return false;
            }

            return node.Children
                .Where(child => !child.TargetKind.IsRelationship())
                .All(Visit);
        }
    }

    private static bool HasUniqueRequestableTargetIdentities(IReadOnlyList<RequestReviewTarget> targets) {
        var identities = new HashSet<(EntityKind Kind, ExternalIdentity Identity)>();
        return targets
            .Where(target => target.Requestable)
            .All(target => identities.Add((target.EntityKind, target.ExternalIdentity)));
    }

    private static void AddChildTargets(
        PluginProvider provider,
        EntityMetadataProposal parent,
        RequestKindDescriptor parentDescriptor,
        ICollection<RequestReviewTarget> targets) {
        var childDescriptor = RequestKindRegistry.ChildOf(parentDescriptor);
        if (childDescriptor is null) {
            return;
        }

        foreach (var child in parent.Children.Where(node => !node.TargetKind.IsRelationship())) {
            if (!IsCompatibleTarget(childDescriptor, child.TargetKind)) {
                continue;
            }

            if (DeclaredIdentityFor(provider, child) is { } identity) {
                AddTarget(child, childDescriptor, identity, targets);
            }

            AddChildTargets(provider, child, childDescriptor, targets);
        }
    }

    private static void AddTarget(
        EntityMetadataProposal proposal,
        RequestKindDescriptor descriptor,
        ExternalIdentity identity,
        ICollection<RequestReviewTarget> targets) {
        if (proposal.Patch is null || string.IsNullOrWhiteSpace(proposal.ProposalId)) {
            return;
        }

        targets.Add(new RequestReviewTarget(
            ProposalId: proposal.ProposalId,
            Kind: descriptor.Kind,
            EntityKind: proposal.TargetKind.ToEntityKind(),
            ExternalIdentity: identity,
            Requestable: descriptor.Committable,
            Position: RequestProposalReading.ChildNumberOf(descriptor.Kind, proposal.Patch),
            Year: RequestProposalReading.YearFromDates(proposal.Patch.Dates),
            Monitored: null));
    }

    private static ExternalIdentity? DeclaredIdentityFor(
        PluginProvider provider,
        EntityMetadataProposal proposal) {
        if (proposal.Patch is null) {
            return null;
        }

        var kindCode = proposal.TargetKind.ToEntityKind().ToCode();
        var namespaces = DeclaredLookupNamespaces(provider, kindCode);
        foreach (var identityNamespace in namespaces) {
            var value = proposal.Patch.ExternalIds
                .FirstOrDefault(pair => pair.Key.Equals(identityNamespace, StringComparison.OrdinalIgnoreCase))
                .Value;
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            try {
                return new ExternalIdentity(identityNamespace, value);
            } catch (ArgumentException) {
                // Invalid or URL-shaped provider values are metadata, not persistent target identities.
            }
        }

        return null;
    }

    private static bool DeclaresLookupIdentity(PluginProvider provider, string kindCode, string identityNamespace) =>
        DeclaredLookupNamespaces(provider, kindCode).Contains(identityNamespace, StringComparer.Ordinal);

    private static bool IsCompatibleTarget(RequestKindDescriptor descriptor, ProposalKind proposalKind) {
        var actualKind = proposalKind.ToEntityKind();
        return actualKind == descriptor.PluginEntityKind
            || descriptor.PluginEntityKind == EntityKind.Movie && actualKind == EntityKind.Video;
    }

    private static IReadOnlyList<string> DeclaredLookupNamespaces(PluginProvider provider, string kindCode) =>
        provider.Supports
            .Where(support =>
                PluginEntityKindCompatibility.SupportsKind(support, kindCode)
                && support.Actions.Contains(LookupIdAction, StringComparer.OrdinalIgnoreCase))
            .SelectMany(support => support.IdentityNamespaces ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Maps a child proposal to a toggleable option with its persistent identity.</summary>
    private static RequestChildOption? MapChild(string identityNamespace, EntityMetadataProposal child, RequestKindDescriptor childDescriptor) {
        if (RequestProposalReading.IdentityValueFor(identityNamespace, child) is not { } workId) {
            return null;
        }

        var itemCount = child.Children.Count(node => !node.TargetKind.IsRelationship());
        return new RequestChildOption(
            Id: RequestProposalReading.FormatQualifiedIdentity(new ExternalIdentity(identityNamespace, workId)),
            Title: child.Patch.Title ?? workId,
            Kind: childDescriptor.Kind,
            Requestable: childDescriptor.Committable,
            // Ordering position when the provider reports one. prism-vocab: external (provider positions vocabulary)
            Number: RequestProposalReading.ChildNumberOf(childDescriptor.Kind, child.Patch),
            Year: RequestProposalReading.YearFromDates(child.Patch.Dates),
            ItemCount: itemCount > 0 ? itemCount : null,
            Overview: child.Patch.Description,
            PosterUrl: RequestProposalReading.BestImage(child),
            Monitored: null);
    }

    /// <summary>Display noun for a container's children, derived from the child kind's wire code ("2 books", "5 albums").</summary>
    private static string ChildNoun(RequestKindDescriptor child, int count) {
        var noun = child.Kind.ToCode();
        return count == 1 ? noun : $"{noun}s";
    }

    private static RequestSearchResult? MapCandidate(
        PluginProvider provider,
        EntitySearchCandidate candidate,
        RequestKindDescriptor descriptor) {
        var identity = CandidateIdentity(provider, candidate, descriptor);
        if (identity is null) {
            return null;
        }

        return new RequestSearchResult(
            ServiceId: Guid.Empty,
            Source: RequestProviderKind.Plugin,
            Kind: descriptor.Kind,
            // Retained for legacy detail/commit clients; canonical callers use PluginId + ExternalIdentity.
            ExternalId: RequestProposalReading.FormatQualifiedIdentity(identity),
            Title: candidate.Title,
            Subtitle: candidate.Source,
            Year: candidate.Year,
            Overview: candidate.Overview,
            PosterUrl: candidate.PosterUrl,
            BackdropUrl: null,
            Rating: candidate.Popularity,
            RuntimeMinutes: null,
            Certification: null,
            TrackCount: null,
            Tags: [],
            Tracked: false,
            UpstreamId: null,
            Monitored: null,
            Requestable: descriptor.Committable,
            ProviderName: provider.Name,
            PluginId: provider.Id,
            ExternalIdentity: identity);
    }

    private static ExternalIdentity? CandidateIdentity(
        PluginProvider provider,
        EntitySearchCandidate candidate,
        RequestKindDescriptor descriptor) {
        var namespaces = provider.Supports
            .Where(support =>
                PluginEntityKindCompatibility.SupportsKind(support, descriptor.PluginKindCode)
                && support.Actions.Contains(SearchAction, StringComparer.OrdinalIgnoreCase))
            .SelectMany(support => support.IdentityNamespaces ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var identityNamespace in namespaces) {
            var value = candidate.ExternalIds
                .FirstOrDefault(pair => pair.Key.Equals(identityNamespace, StringComparison.OrdinalIgnoreCase))
                .Value;
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            try {
                return new ExternalIdentity(identityNamespace, value);
            } catch (ArgumentException) {
                // Search candidates may carry transient URLs; only persistent identities can enter
                // the request/detail/monitor route.
            }
        }

        return null;
    }

    private readonly record struct ProposalCacheKey(
        EntityKind EntityKind,
        string? PluginId,
        ExternalIdentity Identity,
        bool HideNsfw,
        bool IncludeChildren);

    private readonly record struct SelectedSearchProvider(
        PluginProvider Provider,
        PluginDescriptor Descriptor,
        PluginSearchDefinition Schema);

    private readonly record struct ValidatedSearchFields(
        IReadOnlyDictionary<string, string> Fields,
        string? CompatibilityTitle);
}
