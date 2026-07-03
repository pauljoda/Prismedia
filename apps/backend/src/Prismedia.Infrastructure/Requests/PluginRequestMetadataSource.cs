using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Requests;

/// <summary>
/// Runs request-time searches and lookups against enabled plugin metadata providers for every
/// requestable kind, with no library entity involved. Behavior is driven entirely by
/// <see cref="RequestKindDescriptor"/>s: the descriptor says which plugin kind to query and how
/// children behave, so adding a media kind never adds another source class. External ids are
/// provider-qualified ("provider:id") so the request flow can capture both the plugin id and item id
/// for an ID-first acquisition.
/// </summary>
public sealed class PluginRequestMetadataSource(PluginCatalogService catalog, IdentifyRunnerSelector runners)
    : IRequestMetadataSearchSource, IRequestMetadataEnricher, IPluginRequestDetailSource, IPluginRequestProposalSource {
    private static readonly string SearchAction = IdentifyAction.Search.ToCode();
    private static readonly string LookupIdAction = IdentifyAction.LookupId.ToCode();

    public async Task<IReadOnlyList<RequestSearchResult>> SearchAsync(
        RequestKindDescriptor descriptor, string query, bool hideNsfw, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(query)) {
            return [];
        }

        // A committable container (an author, an artist) only makes sense from a provider that can also
        // resolve its children by id — otherwise the per-child acquisition fan-out can't resolve anything.
        var child = RequestKindRegistry.ChildOf(descriptor);
        var requiredChildLookupKind = descriptor.IsContainer && child is { Committable: true } ? child.PluginKindCode : null;

        var providers = (await catalog.ListProvidersAsync(cancellationToken))
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
                ProtocolVersion: 2,
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
                if (MapCandidate(provider.Id, candidate, descriptor) is { } result) {
                    results.Add(result);
                }
            }
        }

        return results;
    }

    public async Task<RequestMetadataEnrichment?> LookupByIdAsync(
        EntityKind kind, string providerId, string externalId, bool hideNsfw, CancellationToken cancellationToken) {
        var proposal = await ResolveProposalCoreAsync(kind, providerId, externalId, hideNsfw, includeChildren: false, cancellationToken);
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
        var (providerId, itemId) = RequestProposalReading.SplitProviderQualifiedId(externalId);
        if (providerId is null || itemId is null) {
            return null;
        }

        // Resolve WITH structural children when the kind offers child options, so a container surfaces
        // its works (an author's books, an artist's albums) — and a series-member book its sibling
        // volumes — as toggleable child options the request fans out into acquisitions.
        var proposal = await ResolveProposalAsync(descriptor, providerId, itemId, hideNsfw,
            includeChildren: descriptor.ChildKind is not null, cancellationToken);
        if (proposal?.Patch is not { } patch) {
            return null;
        }

        var child = RequestKindRegistry.ChildOf(descriptor);
        var children = child is null
            ? []
            : proposal.Children
                .Where(node => !node.TargetKind.IsRelationship())
                .Select(node => MapChild(providerId, node, child))
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
            Title: string.IsNullOrWhiteSpace(patch.Title) ? itemId : patch.Title,
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
        RequestKindDescriptor descriptor, string providerId, string itemId, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) =>
        ResolveProposalCoreAsync(descriptor.PluginEntityKind, providerId, itemId, hideNsfw, includeChildren, cancellationToken);

    /// <summary>
    /// Runs a LookupId for a provider-qualified id through the plugin runner against the given media
    /// kind, gating on the same enabled/NSFW provider rules as the search path. Returns the resolved
    /// proposal, or null when the provider is missing/disabled/NSFW-gated or returns no match.
    /// </summary>
    private async Task<EntityMetadataProposal?> ResolveProposalCoreAsync(
        EntityKind entityKind, string providerId, string externalId, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(externalId)) {
            return null;
        }

        var kindCode = entityKind.ToCode();
        var descriptor = await catalog.FindProviderAsync(providerId, kindCode, cancellationToken);
        if (descriptor is null || !descriptor.Manifest.Supports.Any(support =>
                PluginEntityKindCompatibility.SupportsKind(support, kindCode) && support.Actions.Contains(LookupIdAction))) {
            return null;
        }

        // Mirror the search path's provider gating: only query a provider that is enabled, and never an
        // NSFW-flagged one when NSFW is hidden.
        var provider = (await catalog.ListProvidersAsync(cancellationToken)).FirstOrDefault(p => p.Id == providerId);
        if (provider is null || !provider.Enabled || (hideNsfw && provider.IsNsfw)) {
            return null;
        }

        var ids = new Dictionary<string, string> { [providerId] = externalId };
        var auth = await catalog.GetAuthAsync(descriptor.Manifest, cancellationToken);
        var request = new IdentifyPluginRequest(
            ProtocolVersion: 2,
            Action: IdentifyAction.LookupId,
            Auth: auth,
            // No library entity: a synthetic snapshot carrying the known id is enough for an id lookup.
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

    /// <summary>Maps a child proposal to a toggleable option of the child kind, provider-qualifying its id for the fan-out.</summary>
    private static RequestChildOption? MapChild(string providerId, EntityMetadataProposal child, RequestKindDescriptor childDescriptor) {
        if (RequestProposalReading.WorkIdFor(providerId, child) is not { } workId) {
            return null;
        }

        return new RequestChildOption(
            Id: $"{providerId}:{workId}",
            Title: child.Patch.Title ?? workId,
            Kind: childDescriptor.Kind,
            Requestable: childDescriptor.Committable,
            // Ordering position when the provider reports one. prism-vocab: external (provider positions vocabulary)
            Number: child.Patch.Positions.TryGetValue("volumeNumber", out var volume) ? volume : null,
            Year: RequestProposalReading.YearFromDates(child.Patch.Dates),
            ItemCount: null,
            Overview: child.Patch.Description,
            PosterUrl: RequestProposalReading.BestImage(child),
            Monitored: null);
    }

    /// <summary>Display noun for a container's children, derived from the child kind's wire code ("2 books", "5 albums").</summary>
    private static string ChildNoun(RequestKindDescriptor child, int count) {
        var noun = child.Kind.ToCode();
        return count == 1 ? noun : $"{noun}s";
    }

    private static RequestSearchResult? MapCandidate(string providerId, EntitySearchCandidate candidate, RequestKindDescriptor descriptor) {
        var externalId = candidate.ExternalIds.GetValueOrDefault(providerId) ?? candidate.ExternalIds.Values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(externalId)) {
            return null;
        }

        return new RequestSearchResult(
            ServiceId: Guid.Empty,
            Source: RequestProviderKind.Plugin,
            Kind: descriptor.Kind,
            // Provider-qualified so the request action can recover plugin id + item id.
            ExternalId: $"{providerId}:{externalId}",
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
            Requestable: descriptor.Committable);
    }
}
