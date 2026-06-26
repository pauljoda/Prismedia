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
    : IBookMetadataSearchSource {
    private static readonly string BookKind = EntityKindRegistry.Book.Code;
    private static readonly string SearchAction = IdentifyAction.Search.ToCode();

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
