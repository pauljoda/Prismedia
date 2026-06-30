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
    : IBookMetadataSearchSource, IBookMetadataEnricher, IPluginRequestDetailSource {
    private static readonly string BookKind = EntityKindRegistry.Book.Code;
    private static readonly string SearchAction = IdentifyAction.Search.ToCode();
    private static readonly string LookupIdAction = IdentifyAction.LookupId.ToCode();

    public async Task<IReadOnlyList<RequestSearchResult>> SearchAsync(string query, bool hideNsfw, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(query)) {
            return [];
        }

        var providers = (await catalog.ListProvidersAsync(cancellationToken))
            .Where(provider => provider.Enabled && (!provider.IsNsfw || !hideNsfw))
            .Where(provider => provider.Supports.Any(support =>
                PluginEntityKindCompatibility.SupportsKind(support, BookKind) && support.Actions.Contains(SearchAction)))
            .ToArray();
        if (providers.Length == 0) {
            return [];
        }

        var results = new List<RequestSearchResult>();
        foreach (var provider in providers) {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = await catalog.FindProviderAsync(provider.Id, BookKind, cancellationToken);
            if (descriptor is null) {
                continue;
            }

            var auth = await catalog.GetAuthAsync(descriptor.Manifest, cancellationToken);
            var request = new IdentifyPluginRequest(
                ProtocolVersion: 2,
                Action: IdentifyAction.Search,
                Auth: auth,
                Entity: new IdentifyEntitySnapshot(Guid.NewGuid(), EntityKind.Book, query, new Dictionary<string, string>(), []),
                Query: new IdentifyQuery(query, null, null),
                Hints: new IdentifyMatchHints(new Dictionary<string, string>(), [], query, null),
                StructuralContext: null,
                IncludeNsfw: !hideNsfw,
                IncludeRelationshipDetails: false,
                IncludeStructuralChildren: false);

            var response = await runners.Resolve(descriptor).IdentifyAsync(descriptor, request, cancellationToken);
            foreach (var candidate in response.Result?.Candidates ?? []) {
                if (MapCandidate(provider.Id, candidate) is { } result) {
                    results.Add(result);
                }
            }
        }

        return results;
    }

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

        var author = proposal.Patch.Credits.FirstOrDefault(credit => credit.Role.Contains("author", StringComparison.OrdinalIgnoreCase))?.Name;
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

    /// <summary>
    /// Runs a LookupId for a provider-qualified work id through the plugin runner, gating on the same
    /// enabled/NSFW provider rules as the search path. Returns the resolved proposal, or null when the
    /// provider is missing/disabled/NSFW-gated or returns no match.
    /// </summary>
    private async Task<EntityMetadataProposal?> ResolveProposalAsync(string providerId, string externalId, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(externalId)) {
            return null;
        }

        var descriptor = await catalog.FindProviderAsync(providerId, BookKind, cancellationToken);
        if (descriptor is null || !descriptor.Manifest.Supports.Any(support =>
                PluginEntityKindCompatibility.SupportsKind(support, BookKind) && support.Actions.Contains(LookupIdAction))) {
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
            Entity: new IdentifyEntitySnapshot(Guid.NewGuid(), EntityKind.Book, string.Empty, ids, []),
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

    /// <summary>The best-ranked image url a proposal carries (books generally return cover art), or null.</summary>
    private static string? BestImage(EntityMetadataProposal proposal) =>
        proposal.Images
            .OrderByDescending(image => image.Rank ?? 0)
            .Select(image => image.Url)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

    /// <summary>Splits a provider-qualified id ("provider:itemId") into its parts, or (null, null) when malformed.</summary>
    private static (string? ProviderId, string? ItemId) SplitProviderQualifiedId(string externalId) {
        if (string.IsNullOrWhiteSpace(externalId)) {
            return (null, null);
        }

        var separator = externalId.IndexOf(':');
        if (separator <= 0 || separator >= externalId.Length - 1) {
            return (null, null);
        }

        return (externalId[..separator], externalId[(separator + 1)..]);
    }

    /// <summary>Extracts a 4-digit year from any of the patch's date values (e.g. "2024" or "2024-03-26").</summary>
    private static int? YearFromDates(IReadOnlyDictionary<string, string> dates) {
        foreach (var value in dates.Values) {
            if (value is { Length: >= 4 } && int.TryParse(value[..4], out var year) && year is >= 1000 and <= 9999) {
                return year;
            }
        }

        return null;
    }

    private static RequestSearchResult? MapCandidate(string providerId, EntitySearchCandidate candidate) {
        var externalId = candidate.ExternalIds.GetValueOrDefault(providerId) ?? candidate.ExternalIds.Values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(externalId)) {
            return null;
        }

        return new RequestSearchResult(
            ServiceId: Guid.Empty,
            Source: RequestProviderKind.Plugin,
            Kind: RequestMediaKind.Book,
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
