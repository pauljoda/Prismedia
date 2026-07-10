using System.Globalization;
using Prismedia.Application.Requests;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Requests;

/// <summary>
/// Runs selected-plugin request searches and persistent-identity lookups against enabled metadata
/// providers for every requestable kind, with no library entity involved. Behavior is driven entirely by
/// <see cref="RequestKindDescriptor"/>s: the descriptor says which plugin kind to query and how
/// children behave, so adding a media kind never adds another source class. Identity-only background
/// reads use the central router; interactive review keeps the exact plugin selected during search.
/// </summary>
public sealed class PluginRequestMetadataSource(
    PluginCatalogService catalog,
    IPluginIdentityRouter identityRouter,
    IdentifyRunnerSelector runners)
    : IPluginRequestSearchSource, IRequestMetadataEnricher, IPluginRequestReviewSource,
      IPluginRequestProposalSource {
    private static readonly string SearchAction = IdentifyAction.Search.ToCode();
    private static readonly string LookupIdAction = IdentifyAction.LookupId.ToCode();

    // Resolved proposals are stable on the minutes scale, but every surface that reads one — request
    // review, shared child-monitoring controls, a commit right after review, container
    // sync — used to pay a fresh plugin round-trip each time. A short process-wide TTL cache makes
    // repeat reads instant without holding provider data long enough to go stale. The source is
    // scoped, hence the static cache; the cap bounds memory (proposals with children can be large).
    private static readonly TimeSpan ProposalTtl = TimeSpan.FromMinutes(15);
    private const int ProposalCacheCap = 128;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        ProposalCacheKey,
        (DateTimeOffset At, RoutedRequestProposal Resolved)> ProposalCache = new();

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
        var proposal = (await ResolveProposalCoreAsync(
            kind,
            identity,
            hideNsfw,
            includeChildren: false,
            cancellationToken))?.Proposal;
        if (proposal?.Patch is not { } patch) {
            return null;
        }

        return new RequestMetadataEnrichment(
            patch.Description,
            RequestProposalReading.BestImage(proposal),
            RequestProposalReading.YearFromDates(patch.Dates));
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
            || !MatchesExplicitRoute(proposal, route)
            || !DeclaresLookupIdentity(provider, proposal.TargetKind.ToEntityKind().ToCode(), route.Identity.Namespace)
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

    public Task<RoutedRequestProposal?> ResolveProposalAsync(
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
    private async Task<RoutedRequestProposal?> ResolveProposalCoreAsync(
        EntityKind entityKind, ExternalIdentity identity, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) {
        var cacheKey = new ProposalCacheKey(entityKind, PluginId: null, identity, hideNsfw, includeChildren);
        if (ProposalCache.TryGetValue(cacheKey, out var hit) && DateTimeOffset.UtcNow - hit.At < ProposalTtl) {
            return hit.Resolved;
        }

        var resolved = await ResolveProposalUncachedAsync(entityKind, identity, hideNsfw, includeChildren, cancellationToken);
        // Only successful resolutions are cached: a transient provider failure should retry, and a
        // gated/unknown id is cheap to re-answer.
        if (resolved is not null) {
            EvictForCapacity();
            ProposalCache[cacheKey] = (DateTimeOffset.UtcNow, resolved);
        }

        return resolved;
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
            if (MatchesExplicitRoute(hit.Resolved.Proposal, route)) {
                return hit.Resolved.Proposal;
            }

            ProposalCache.TryRemove(cacheKey, out _);
        }

        var proposal = await RunRouteAsync(entityKind, route, hideNsfw, includeChildren, cancellationToken);
        if (proposal is null || !MatchesExplicitRoute(proposal, route)) {
            return null;
        }

        EvictForCapacity();
        ProposalCache[cacheKey] = (
            DateTimeOffset.UtcNow,
            new RoutedRequestProposal(route, proposal));
        return proposal;
    }

    private static bool MatchesExplicitRoute(
        EntityMetadataProposal? proposal,
        PluginIdentityRoute route) =>
        proposal?.Patch is not null
        && string.Equals(proposal.Provider, route.PluginId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            RequestProposalReading.IdentityValueFor(route.Identity.Namespace, proposal),
            route.Identity.Value,
            StringComparison.Ordinal);

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

    private async Task<RoutedRequestProposal?> ResolveProposalUncachedAsync(
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
                return new RoutedRequestProposal(route, proposal);
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
            // Retained for the older commit contract; canonical callers use PluginId + ExternalIdentity.
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
                // the request/review/monitor route.
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
