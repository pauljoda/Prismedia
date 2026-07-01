using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Requests;

/// <summary>
/// Runs free-text book searches against enabled book-capable plugin providers (e.g. OpenLibrary) at
/// request time, with no library entity. Synthesizes a book entity snapshot, runs an Action=Search
/// identify request through the plugin runner, and maps candidates to <see cref="RequestSearchResult"/>.
/// The external id is provider-qualified ("provider:id") so the request flow can capture both the plugin
/// id and item id for an ID-first acquisition.
/// </summary>
public sealed class PluginBookMetadataSearchSource(PluginCatalogService catalog, IdentifyRunnerSelector runners)
    : IBookMetadataSearchSource, IAuthorMetadataSearchSource, IBookMetadataEnricher, IPluginRequestDetailSource, IPluginRequestProposalSource {
    private static readonly string BookKind = EntityKindRegistry.Book.Code;
    private static readonly string PersonKind = EntityKindRegistry.Person.Code;
    private static readonly string SearchAction = IdentifyAction.Search.ToCode();
    private static readonly string LookupIdAction = IdentifyAction.LookupId.ToCode();

    public Task<IReadOnlyList<RequestSearchResult>> SearchAsync(string query, bool hideNsfw, CancellationToken cancellationToken) =>
        SearchKindAsync(query, BookKind, EntityKind.Book, RequestMediaKind.Book, hideNsfw, cancellationToken);

    public Task<IReadOnlyList<RequestSearchResult>> SearchAuthorsAsync(string query, bool hideNsfw, CancellationToken cancellationToken) =>
        // An "author" request only makes sense for a provider that can also enumerate the author's books, so
        // require book support too — this keeps movie/TV people (e.g. a TMDB person) out of the author results.
        SearchKindAsync(query, PersonKind, EntityKind.Person, RequestMediaKind.Author, hideNsfw, cancellationToken, alsoSupportsKind: BookKind);

    /// <summary>
    /// Runs a free-text Action=Search across enabled plugin providers that support <paramref name="kindCode"/>,
    /// mapping each candidate to a <see cref="RequestSearchResult"/> tagged <paramref name="resultKind"/>.
    /// When <paramref name="alsoSupportsKind"/> is set, only providers that additionally support that kind are
    /// queried (e.g. authors require book support so an acquisition can fan out).
    /// </summary>
    private async Task<IReadOnlyList<RequestSearchResult>> SearchKindAsync(
        string query, string kindCode, EntityKind entityKind, RequestMediaKind resultKind, bool hideNsfw, CancellationToken cancellationToken,
        string? alsoSupportsKind = null) {
        if (string.IsNullOrWhiteSpace(query)) {
            return [];
        }

        var providers = (await catalog.ListProvidersAsync(cancellationToken))
            .Where(provider => provider.Enabled && (!provider.IsNsfw || !hideNsfw))
            .Where(provider => provider.Supports.Any(support =>
                PluginEntityKindCompatibility.SupportsKind(support, kindCode) && support.Actions.Contains(SearchAction)))
            // When a cross-kind requirement is set (authors require books), the provider must be able to
            // LookupId that kind too — otherwise the per-child acquisition fan-out can't resolve anything.
            .Where(provider => alsoSupportsKind is null || provider.Supports.Any(support =>
                PluginEntityKindCompatibility.SupportsKind(support, alsoSupportsKind) && support.Actions.Contains(LookupIdAction)))
            .ToArray();
        if (providers.Length == 0) {
            return [];
        }

        var results = new List<RequestSearchResult>();
        foreach (var provider in providers) {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = await catalog.FindProviderAsync(provider.Id, kindCode, cancellationToken);
            if (descriptor is null) {
                continue;
            }

            var auth = await catalog.GetAuthAsync(descriptor.Manifest, cancellationToken);
            var request = new IdentifyPluginRequest(
                ProtocolVersion: 2,
                Action: IdentifyAction.Search,
                Auth: auth,
                Entity: new IdentifyEntitySnapshot(Guid.NewGuid(), entityKind, query, new Dictionary<string, string>(), []),
                Query: new IdentifyQuery(query, null, null),
                Hints: new IdentifyMatchHints(new Dictionary<string, string>(), [], query, null),
                StructuralContext: null,
                IncludeNsfw: !hideNsfw,
                IncludeRelationshipDetails: false,
                IncludeStructuralChildren: false);

            var response = await runners.Resolve(descriptor).IdentifyAsync(descriptor, request, cancellationToken);
            foreach (var candidate in response.Result?.Candidates ?? []) {
                if (MapCandidate(provider.Id, candidate, resultKind) is { } result) {
                    results.Add(result);
                }
            }
        }

        return results;
    }

    /// <summary>The raw book proposal for a request commit — same resolve path as the detail surface, no mapping-down.</summary>
    public Task<EntityMetadataProposal?> ResolveBookProposalAsync(string providerId, string itemId, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) =>
        ResolveProposalAsync(providerId, itemId, hideNsfw, includeChildren, cancellationToken);

    /// <summary>The raw author proposal (works enumerated as children) for a request commit.</summary>
    public Task<EntityMetadataProposal?> ResolveAuthorProposalAsync(string providerId, string itemId, bool hideNsfw, CancellationToken cancellationToken) =>
        ResolveProposalAsync(providerId, itemId, hideNsfw, includeChildren: true, cancellationToken, PersonKind, EntityKind.Person);

    public async Task<BookMetadataEnrichment?> LookupByIdAsync(string providerId, string externalId, bool hideNsfw, CancellationToken cancellationToken) {
        var proposal = await ResolveProposalAsync(providerId, externalId, hideNsfw, includeChildren: false, cancellationToken);
        if (proposal?.Patch is not { } patch) {
            return null;
        }

        return new BookMetadataEnrichment(patch.Description, BestImage(proposal), YearFromDates(patch.Dates));
    }

    public async Task<RequestDetailResponse?> GetBookDetailAsync(string externalId, bool hideNsfw, CancellationToken cancellationToken) {
        var (providerId, itemId) = SplitProviderQualifiedId(externalId);
        if (providerId is null || itemId is null) {
            return null;
        }

        // Resolve WITH structural children so a book that belongs to a series surfaces its sibling volumes as
        // toggleable child options (the request fans each selected one out into its own acquisition).
        var proposal = await ResolveProposalAsync(providerId, itemId, hideNsfw, includeChildren: true, cancellationToken);
        if (proposal?.Patch is not { } patch) {
            return null;
        }

        var author = RequestProposalReading.AuthorFromCredits(patch);
        var children = proposal.Children
            .Select(child => MapChild(providerId, child))
            .Where(child => child is not null)
            .Select(child => child!)
            .ToArray();

        return new RequestDetailResponse(
            Source: RequestProviderKind.Plugin,
            Kind: RequestMediaKind.Book,
            ExternalId: externalId,
            Title: string.IsNullOrWhiteSpace(patch.Title) ? itemId : patch.Title,
            Subtitle: author,
            Year: YearFromDates(patch.Dates),
            Overview: patch.Description,
            PosterUrl: BestImage(proposal),
            BackdropUrl: null,
            Rating: null,
            RuntimeMinutes: null,
            Certification: null,
            TrackCount: null,
            Tags: [],
            Studios: [],
            Credits: [],
            Cast: [],
            Ratings: [],
            Children: children,
            Tracks: [],
            Tracked: false,
            UpstreamId: null,
            Monitored: null,
            ServiceOptions: new RequestServiceOptionsResponse([], [], [], []));
    }

    public async Task<RequestDetailResponse?> GetAuthorDetailAsync(string externalId, bool hideNsfw, CancellationToken cancellationToken) {
        var (providerId, authorId) = SplitProviderQualifiedId(externalId);
        if (providerId is null || authorId is null) {
            return null;
        }

        // Resolve the author WITH structural children so the plugin enumerates the author's books, which become
        // toggleable child options — the request fans each selected one out into its own book acquisition.
        var proposal = await ResolveProposalAsync(
            providerId, authorId, hideNsfw, includeChildren: true, cancellationToken, PersonKind, EntityKind.Person);
        if (proposal?.Patch is not { } patch) {
            return null;
        }

        var children = proposal.Children
            .Select(child => MapChild(providerId, child))
            .Where(child => child is not null)
            .Select(child => child!)
            .ToArray();

        return new RequestDetailResponse(
            Source: RequestProviderKind.Plugin,
            Kind: RequestMediaKind.Author,
            ExternalId: externalId,
            Title: string.IsNullOrWhiteSpace(patch.Title) ? authorId : patch.Title,
            Subtitle: children.Length > 0 ? $"{children.Length} book{(children.Length == 1 ? "" : "s")}" : null,
            Year: YearFromDates(patch.Dates),
            Overview: patch.Description,
            PosterUrl: BestImage(proposal),
            BackdropUrl: null,
            Rating: null,
            RuntimeMinutes: null,
            Certification: null,
            TrackCount: null,
            Tags: [],
            Studios: [],
            Credits: [],
            Cast: [],
            Ratings: [],
            Children: children,
            Tracks: [],
            Tracked: false,
            UpstreamId: null,
            Monitored: null,
            ServiceOptions: new RequestServiceOptionsResponse([], [], [], []));
    }

    /// <summary>
    /// Runs a LookupId for a provider-qualified id through the plugin runner against <paramref name="kindCode"/>,
    /// gating on the same enabled/NSFW provider rules as the search path. Returns the resolved proposal, or null
    /// when the provider is missing/disabled/NSFW-gated or returns no match.
    /// </summary>
    private async Task<EntityMetadataProposal?> ResolveProposalAsync(
        string providerId, string externalId, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken,
        string? kindCode = null, EntityKind entityKind = EntityKind.Book) {
        kindCode ??= BookKind;
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(externalId)) {
            return null;
        }

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

    /// <summary>Maps a series volume proposal to a toggleable child option, provider-qualifying its id for the acquisition fan-out.</summary>
    private static RequestChildOption? MapChild(string providerId, EntityMetadataProposal child) {
        var workId = child.Patch.ExternalIds.GetValueOrDefault(providerId) ?? child.Patch.ExternalIds.Values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(workId)) {
            return null;
        }

        return new RequestChildOption(
            Id: $"{providerId}:{workId}",
            Title: child.Patch.Title ?? workId,
            Kind: RequestMediaKind.Book,
            Requestable: true,
            Number: child.Patch.Positions.TryGetValue("volumeNumber", out var volume) ? volume : null,
            Year: YearFromDates(child.Patch.Dates),
            ItemCount: null,
            Overview: child.Patch.Description,
            PosterUrl: BestImage(child),
            Monitored: null);
    }

    // Provider-qualified id splitting and the small proposal projections are shared with the request
    // commit through RequestProposalReading, so the two surfaces read proposals identically.
    private static string? BestImage(EntityMetadataProposal proposal) =>
        RequestProposalReading.BestImage(proposal);

    private static (string? ProviderId, string? ItemId) SplitProviderQualifiedId(string externalId) =>
        RequestProposalReading.SplitProviderQualifiedId(externalId);

    private static int? YearFromDates(IReadOnlyDictionary<string, string> dates) =>
        RequestProposalReading.YearFromDates(dates);

    private static RequestSearchResult? MapCandidate(string providerId, EntitySearchCandidate candidate, RequestMediaKind kind) {
        var externalId = candidate.ExternalIds.GetValueOrDefault(providerId) ?? candidate.ExternalIds.Values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(externalId)) {
            return null;
        }

        return new RequestSearchResult(
            ServiceId: Guid.Empty,
            Source: RequestProviderKind.Plugin,
            Kind: kind,
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
            Requestable: true);
    }
}
