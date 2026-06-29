namespace Prismedia.Application.Acquisition;

/// <summary>
/// Runs the indexer search for an acquisition: queries every enabled indexer concurrently, scores the
/// combined releases against the default profile, and reports per-indexer failures. Pure orchestration
/// over the ports — the background <c>AcquisitionSearch</c> job persists the outcome.
/// </summary>
public sealed class AcquisitionSearchRunner(
    IIndexerConfigStore indexers,
    IIndexerSearchClientFactory clients,
    IBookAcquisitionProfileStore profiles,
    IAcquisitionBlocklistStore blocklist,
    IBookReleaseDecisionEngine decisionEngine) {
    public async Task<AcquisitionSearchOutcome> RunAsync(AcquisitionSearchInput input, CancellationToken cancellationToken) {
        var text = BuildQueryText(input.Title, input.Author);
        if (string.IsNullOrWhiteSpace(text)) {
            return new AcquisitionSearchOutcome([], []);
        }

        var configs = (await indexers.ListDetailsAsync(cancellationToken))
            .Where(config => config.Enabled)
            .ToArray();
        if (configs.Length == 0) {
            return new AcquisitionSearchOutcome([], []);
        }

        var rules = await profiles.GetDefaultRulesAsync(cancellationToken);
        var blocklisted = await blocklist.GetIdentitiesAsync(cancellationToken);
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

        return new AcquisitionSearchOutcome(decisionEngine.Evaluate(releases, rules, blocklisted), errors);
    }

    private async Task<(Contracts.Acquisition.IndexerConfigDetail Config, IReadOnlyList<IndexerRelease> Found, string? Error)> SearchIndexerAsync(
        Contracts.Acquisition.IndexerConfigDetail config,
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

    private static string BuildQueryText(string title, string? author) {
        var trimmedTitle = title?.Trim() ?? string.Empty;
        var trimmedAuthor = author?.Trim();
        return string.IsNullOrWhiteSpace(trimmedAuthor) ? trimmedTitle : $"{trimmedTitle} {trimmedAuthor}".Trim();
    }
}
