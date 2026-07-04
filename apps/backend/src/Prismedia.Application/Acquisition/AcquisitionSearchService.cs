using Prismedia.Application.Settings;

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
    IDownloadClientConfigStore downloadClients,
    IIndexerStatusStore indexerStatuses,
    IndexerQueryWindow queryWindow,
    IAcquisitionDecisionEngineFactory decisionEngines,
    SettingsService settings) {
    /// <summary>
    /// The per-priority-step score adjustment that breaks exact score ties in favor of the preferred
    /// indexer. Orders of magnitude below any real score difference, so it can never reorder releases
    /// the engine actually distinguished.
    /// </summary>
    private const double PriorityTieBreak = 1e-9;
    /// <param name="upgradeOwnedQuality">
    /// When set, runs this as an upgrade search: the engine accepts only releases that strictly beat this
    /// owned quality (in the kind's vocabulary — a book rank or a media ladder code) and never downgrade the
    /// format. Null for an ordinary first-grab search.
    /// </param>
    public async Task<AcquisitionSearchOutcome> RunAsync(AcquisitionSearchInput input, CancellationToken cancellationToken, UpgradeOwnedQuality? upgradeOwnedQuality = null) {
        var queries = ReleaseQueryLadder.For(input);
        if (queries.Count == 0) {
            return new AcquisitionSearchOutcome([], []);
        }

        // An indexer inside its failure-backoff window is skipped for this search rather than
        // contributing the same error to every pass; it rejoins automatically when the window closes.
        var health = await indexerStatuses.GetAllAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var configs = (await indexers.ListDetailsAsync(cancellationToken))
            .Where(config => config.Enabled && !(health.GetValueOrDefault(config.Id)?.IsDisabledAt(now) ?? false))
            .ToArray();
        if (configs.Length == 0) {
            return new AcquisitionSearchOutcome([], []);
        }

        var rules = await profiles.GetRulesAsync(input.ProfileId, input.Kind, cancellationToken);

        // The proper/repack policy is an app-global fact set per search (never by a profile), the same way
        // the protocol and TV-unit facts ride the rules: it feeds the pure scoring/upgrade functions so a
        // proper ranks (and upgrades) exactly as the setting dictates.
        var properPolicy = (await settings.GetProperDownloadSettingsAsync(cancellationToken)).Policy;
        rules = rules with { ProperPolicy = properPolicy };

        if (upgradeOwnedQuality is { } owned) {
            // IsUpgradeSearch is the single truth for whether the upgrade gates apply; a non-null record means
            // this is an upgrade search regardless of which vocabulary axis carries the owned quality. The book
            // gate reads OwnedQuality (default = Floor when the child is a media kind, harmlessly ignored) and
            // the media gate reads OwnedMediaQuality (+ OwnedMediaRevision for the same-quality proper case).
            rules = rules with {
                IsUpgradeSearch = true,
                OwnedQuality = owned.BookRank ?? default,
                OwnedMediaQuality = owned.MediaQualityCode,
                OwnedMediaRevision = owned.MediaRevision,
                OwnedFormatScore = owned.FormatScore
            };
        }

        // A release protocol is acceptable when an enabled download client can acquire it. With no
        // client configured yet the torrent-only default stands, so candidates still surface for review.
        var protocols = await downloadClients.GetEnabledProtocolsAsync(cancellationToken);
        if (protocols.Count > 0) {
            rules = rules with { AllowedProtocols = protocols };
        }

        // TV unit context rides the rules the same way the upgrade fields do: set per search from the
        // acquisition, never by a profile, so the unit-match specification knows what is sought.
        if (input.SeasonNumber is not null) {
            rules = rules with { SeasonNumber = input.SeasonNumber, EpisodeNumber = input.EpisodeNumber };
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
            await RecordHealthAsync(searches, cancellationToken);

            var releases = new List<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)>();
            var errors = new List<IndexerSearchError>();
            foreach (var search in searches) {
                foreach (var release in search.Found) {
                    releases.Add((release, search.Config.Id, search.Config.DisplayName));
                }

                if (search.Error is not null) {
                    errors.Add(new IndexerSearchError(search.Config.Id, search.Config.DisplayName, search.Error));
                }
            }

            outcome = new AcquisitionSearchOutcome(WithIndexerPriorityTieBreak(engine.Evaluate(releases, rules, blocklisted), configs), errors);
            if (outcome.Candidates.Any(candidate => candidate.Accepted)) {
                return outcome;
            }
        }

        return outcome;
    }

    /// <summary>
    /// Folds indexer priority into the scalar score as an exact-tie break: identical releases from two
    /// indexers rank the preferred (lower-priority-number) indexer first, and every downstream consumer
    /// that sorts by score (auto-grab, the review list) inherits the ordering for free.
    /// </summary>
    private static IReadOnlyList<ScoredRelease> WithIndexerPriorityTieBreak(
        IReadOnlyList<ScoredRelease> candidates,
        IReadOnlyList<Contracts.Acquisition.IndexerConfigDetail> configs) {
        var priorityById = configs.ToDictionary(config => config.Id, config => config.Priority);
        return candidates
            .Select(candidate => candidate.IndexerConfigId is { } id && priorityById.TryGetValue(id, out var priority)
                ? candidate with { Score = candidate.Score - priority * PriorityTieBreak }
                : candidate)
            .OrderByDescending(candidate => candidate.Accepted)
            .ThenByDescending(candidate => candidate.Score)
            .ToArray();
    }

    /// <summary>One indexer's contribution to a search rung. A rate-limited skip carries a message but is not a failure.</summary>
    private sealed record IndexerSearchResult(
        Contracts.Acquisition.IndexerConfigDetail Config,
        IReadOnlyList<IndexerRelease> Found,
        string? Error,
        bool RateLimited = false);

    private async Task<IndexerSearchResult> SearchIndexerAsync(
        Contracts.Acquisition.IndexerConfigDetail config,
        string text,
        Domain.Entities.EntityKind kind,
        CancellationToken cancellationToken) {
        // A rate-limited skip is surfaced (so a thin result set is explainable) but is NOT a failure —
        // it must not climb the backoff ladder.
        if (!queryWindow.TryRecordQuery(config.Id, config.QueryLimitPerHour)) {
            return new IndexerSearchResult(config, [], "Hourly query limit reached; this indexer was skipped for this search.", RateLimited: true);
        }

        try {
            // Narrow the indexer's configured categories to the acquisition kind's Torznab range, so a
            // movie or album search never queries the book categories the indexer was set up with.
            var categories = TorznabCategories.ForKind(kind, config.Categories);
            var connection = new IndexerConnection(config.Id, config.Kind, config.BaseUrl, config.ApiKey, categories);
            var found = await clients.Get(config.Kind).SearchAsync(connection, new IndexerQuery(text, categories), cancellationToken);
            return new IndexerSearchResult(config, found, null);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new IndexerSearchResult(config, [], ex.Message);
        }
    }

    /// <summary>
    /// Records each indexer's health outcome sequentially — the searches themselves fan out, but the
    /// status store shares one DbContext, which must never see concurrent operations. A rate-limit skip
    /// neither climbs nor descends the ladder.
    /// </summary>
    private async Task RecordHealthAsync(IEnumerable<IndexerSearchResult> searches, CancellationToken cancellationToken) {
        foreach (var search in searches) {
            if (search.RateLimited) {
                continue;
            }

            if (search.Error is null) {
                await indexerStatuses.RecordSuccessAsync(search.Config.Id, cancellationToken);
            } else {
                await indexerStatuses.RecordFailureAsync(search.Config.Id, search.Error, cancellationToken);
            }
        }
    }
}
