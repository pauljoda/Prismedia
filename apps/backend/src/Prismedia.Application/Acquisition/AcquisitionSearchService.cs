using Prismedia.Contracts.Acquisition;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Searches all enabled indexers for a book and returns release candidates scored against the
/// default acquisition profile. Indexer failures are surfaced as errors rather than failing the
/// whole search, so partial results stay usable.
/// </summary>
public sealed class AcquisitionSearchService(
    IIndexerConfigStore indexers,
    IIndexerSearchClientFactory clients,
    IBookAcquisitionProfileStore profiles,
    IBookReleaseDecisionEngine decisionEngine) {
    public async Task<AcquisitionSearchResponse> SearchAsync(AcquisitionSearchRequest request, CancellationToken cancellationToken) {
        var text = BuildQueryText(request);
        if (string.IsNullOrWhiteSpace(text)) {
            return new AcquisitionSearchResponse([], []);
        }

        var configs = (await indexers.ListDetailsAsync(cancellationToken))
            .Where(config => config.Enabled)
            .ToArray();
        if (configs.Length == 0) {
            return new AcquisitionSearchResponse([], []);
        }

        var rules = await profiles.GetDefaultRulesAsync(cancellationToken);

        var searches = await Task.WhenAll(configs.Select(config => SearchIndexerAsync(config, text, cancellationToken)));

        var releases = new List<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)>();
        var errors = new List<IndexerSearchError>();
        foreach (var (config, found, error) in searches) {
            foreach (var release in found) {
                releases.Add((release, config.Id, config.DisplayName));
            }

            if (error is not null) {
                errors.Add(new IndexerSearchError(config.Id, config.DisplayName, error));
            }
        }

        var scored = decisionEngine.Evaluate(releases, rules);
        var candidates = scored.Select(ToView).ToArray();
        return new AcquisitionSearchResponse(candidates, errors);
    }

    private async Task<(IndexerConfigDetail Config, IReadOnlyList<IndexerRelease> Found, string? Error)> SearchIndexerAsync(
        IndexerConfigDetail config,
        string text,
        CancellationToken cancellationToken) {
        try {
            var connection = new IndexerConnection(config.Id, config.Kind, config.BaseUrl, config.ApiKey, config.Categories);
            var found = await clients.Get(config.Kind).SearchAsync(connection, new IndexerQuery(text, config.Categories), cancellationToken);
            return (config, found, null);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return (config, [], ex.Message);
        }
    }

    private static string BuildQueryText(AcquisitionSearchRequest request) {
        var title = request.Title?.Trim() ?? string.Empty;
        var author = request.Author?.Trim();
        return string.IsNullOrWhiteSpace(author) ? title : $"{title} {author}".Trim();
    }

    private static ReleaseCandidateView ToView(ScoredRelease scored) {
        var release = scored.Release;
        return new ReleaseCandidateView(
            scored.IndexerName,
            release.Title,
            release.SizeBytes,
            release.Seeders,
            release.Peers,
            release.Protocol,
            scored.Accepted,
            scored.Score,
            scored.Rejections,
            release.MagnetUrl,
            release.DownloadUrl,
            release.InfoUrl,
            release.PublishedAt);
    }
}
