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
    IAcquisitionDecisionEngineFactory decisionEngines) {
    /// <param name="upgradeOwnedQuality">
    /// When set, runs this as an upgrade search: the engine accepts only releases that strictly beat this
    /// owned quality (and never downgrade the format). Null for an ordinary first-grab search.
    /// </param>
    public async Task<AcquisitionSearchOutcome> RunAsync(AcquisitionSearchInput input, CancellationToken cancellationToken, Domain.Entities.BookQualityRank? upgradeOwnedQuality = null) {
        var queries = ReleaseQueryLadder.For(input);
        if (queries.Count == 0) {
            return new AcquisitionSearchOutcome([], []);
        }

        var configs = (await indexers.ListDetailsAsync(cancellationToken))
            .Where(config => config.Enabled)
            .ToArray();
        if (configs.Length == 0) {
            return new AcquisitionSearchOutcome([], []);
        }

        var rules = await profiles.GetDefaultRulesAsync(cancellationToken);
        if (upgradeOwnedQuality is { } owned) {
            rules = rules with { IsUpgradeSearch = true, OwnedQuality = owned };
        }

        var blocklisted = await blocklist.GetIdentitiesAsync(cancellationToken);
        var engine = decisionEngines.Get(input.Kind);

        // Walk the query ladder: the first rung whose results include an acceptable release wins. A
        // rung that found only rejects falls through to the next, broader phrasing; the last rung's
        // outcome (candidates and all) is returned regardless, so the review UI always has something
        // transparent to show.
        AcquisitionSearchOutcome outcome = new([], []);
        foreach (var text in queries) {
            var searches = await Task.WhenAll(configs.Select(config => SearchIndexerAsync(config, text, input.Kind, cancellationToken)));

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

            outcome = new AcquisitionSearchOutcome(engine.Evaluate(releases, rules, blocklisted), errors);
            if (outcome.Candidates.Any(candidate => candidate.Accepted)) {
                return outcome;
            }
        }

        return outcome;
    }

    private async Task<(Contracts.Acquisition.IndexerConfigDetail Config, IReadOnlyList<IndexerRelease> Found, string? Error)> SearchIndexerAsync(
        Contracts.Acquisition.IndexerConfigDetail config,
        string text,
        Domain.Entities.EntityKind kind,
        CancellationToken cancellationToken) {
        try {
            // Narrow the indexer's configured categories to the acquisition kind's Torznab range, so a
            // movie or album search never queries the book categories the indexer was set up with.
            var categories = TorznabCategories.ForKind(kind, config.Categories);
            var connection = new IndexerConnection(config.Id, config.Kind, config.BaseUrl, config.ApiKey, categories);
            var found = await clients.Get(config.Kind).SearchAsync(connection, new IndexerQuery(text, categories), cancellationToken);
            return (config, found, null);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return (config, [], ex.Message);
        }
    }

}
