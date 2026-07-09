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
/// children behave, so adding a media kind never adds another source class. Request ids are qualified
/// by persistent identity namespace; the central router independently selects the plugin that handles it.
/// </summary>
public sealed class PluginRequestMetadataSource(
    PluginCatalogService catalog,
    IPluginIdentityRouter identityRouter,
    IdentifyRunnerSelector runners)
    : IRequestMetadataSearchSource, IRequestMetadataEnricher, IPluginRequestDetailSource, IPluginRequestProposalSource {
    private static readonly string SearchAction = IdentifyAction.Search.ToCode();
    private static readonly string LookupIdAction = IdentifyAction.LookupId.ToCode();

    // Resolved proposals are stable on the minutes scale, but every surface that reads one — request
    // detail pages, the series page's Season Pass options, a commit right after review, container
    // sync — used to pay a fresh plugin round-trip each time. A short process-wide TTL cache makes
    // repeat reads instant without holding provider data long enough to go stale. The source is
    // scoped, hence the static cache; the cap bounds memory (proposals with children can be large).
    private static readonly TimeSpan ProposalTtl = TimeSpan.FromMinutes(15);
    private const int ProposalCacheCap = 128;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset At, EntityMetadataProposal Proposal)> ProposalCache = new();

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

            var auth = await catalog.GetAuthAsync(descriptorForProvider.Manifest, cancellationToken);
            var request = new IdentifyPluginRequest(
                ProtocolVersion: PluginProtocol.CurrentVersion,
                Action: IdentifyAction.Search,
                Auth: auth,
                Entity: new IdentifyEntitySnapshot(Guid.NewGuid(), descriptor.PluginEntityKind, query, new Dictionary<string, string>(), []),
                Query: new IdentifyQuery(query, null, null),
                Hints: new IdentifyMatchHints(new Dictionary<string, string>(), [], query, null),
                StructuralContext: null,
                IncludeNsfw: !hideNsfw,
                IncludeRelationshipDetails: false,
                IncludeStructuralChildren: false);

            var response = await runners.Resolve(descriptorForProvider).IdentifyAsync(descriptorForProvider, request, cancellationToken);
            foreach (var candidate in response.Result?.Candidates ?? []) {
                if (MapCandidate(provider, candidate, descriptor) is { } result) {
                    results.Add(result);
                }
            }
        }

        return results;
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

    public Task<EntityMetadataProposal?> ResolveProposalAsync(
        RequestKindDescriptor descriptor, ExternalIdentity identity, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) =>
        ResolveProposalCoreAsync(descriptor.PluginEntityKind, identity, hideNsfw, includeChildren, cancellationToken);

    /// <summary>
    /// Routes a persistent identity to every capable LookupId plugin for the given media kind, gating
    /// on the same enabled/NSFW rules as search. Shared-namespace routes are tried deterministically.
    /// </summary>
    private async Task<EntityMetadataProposal?> ResolveProposalCoreAsync(
        EntityKind entityKind, ExternalIdentity identity, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) {
        var cacheKey = string.Join('|', entityKind.ToCode(), identity.Namespace, identity.Value, hideNsfw, includeChildren);
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

            var descriptor = await catalog.FindProviderAsync(route.PluginId, kindCode, cancellationToken);
            if (descriptor is null) {
                continue;
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
            if (response.Result is not null) {
                return response.Result;
            }
        }

        return null;
    }

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
            // Identity-qualified; plugin selection remains a server-side routing concern.
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
            ProviderName: provider.Name);
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
}
