using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;

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
    IAcquisitionPolicyRegistry policies,
    SettingsService settings) {
    /// <param name="upgradeOwnedQuality">
    /// When set, runs this as an upgrade search: the engine accepts only releases that strictly beat this
    /// owned quality (in the kind's vocabulary — a book rank or a media ladder code) and never downgrade the
    /// format. Null for an ordinary first-grab search.
    /// </param>
    public async Task<AcquisitionSearchOutcome> RunAsync(
        AcquisitionSearchInput input,
        CancellationToken cancellationToken,
        UpgradeOwnedQuality? upgradeOwnedQuality = null,
        string? customQuery = null) {
        if (string.IsNullOrWhiteSpace(input.Title)) {
            return new AcquisitionSearchOutcome([], []);
        }

        var policy = policies.Get(input.Kind);
        var queries = string.IsNullOrWhiteSpace(customQuery)
            ? policy.BuildQueries(input)
            : [customQuery.Trim()];

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

        var rules = (await profiles.GetRulesAsync(input.ProfileId, input.Kind, cancellationToken)) with {
            TargetTitle = input.WorkTitle,
            TargetYear = input.Year,
            TargetAuthor = input.Author,
            BookRendition = input.BookRendition
        };

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

        // Results are actionable only when an enabled download client speaks their protocol. An empty
        // capability set therefore produces no results rather than advertising releases that cannot be
        // queued. A sole protocol also overrides any stale preference automatically.
        var protocols = (await downloadClients.GetEnabledProtocolsAsync(cancellationToken)).Distinct().ToArray();
        if (protocols.Length == 0) {
            return new AcquisitionSearchOutcome([], []);
        }
        rules = rules with { AllowedProtocols = protocols };
        var preferredProtocol = await AcquisitionProtocolPreference.ResolveAsync(downloadClients, settings, cancellationToken)
            ?? protocols[0];

        // TV unit context rides the rules the same way the upgrade fields do: set per search from the
        // acquisition, never by a profile, so the unit-match specification knows what is sought.
        if (input.SeasonNumber is not null) {
            rules = rules with { SeasonNumber = input.SeasonNumber, EpisodeNumber = input.EpisodeNumber };
        }

        // Book/comic unit context, same pattern: the sought volume gates wrong-volume releases.
        if (input.VolumeNumber is not null) {
            rules = rules with { VolumeNumber = input.VolumeNumber };
        }

        var blocklisted = await blocklist.GetIdentitiesAsync(cancellationToken);
        var engine = policy.DecisionEngineFor(input.Kind);

        // Every query variant contributes to one decision set. Stopping at the first acceptable rung
        // made indexer query wording decide the winner before quality, formats, protocol, health and
        // priority could be compared. Arr-style search instead aggregates and de-duplicates the full
        // applicable set, then makes one global decision.
        var releases = new List<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)>();
        var errors = new Dictionary<Guid, IndexerSearchError>();
        var failedIndexers = new HashSet<Guid>();
        foreach (var text in queries) {
            var searchable = configs.Where(config => !failedIndexers.Contains(config.Id)).ToArray();
            if (searchable.Length == 0) {
                break;
            }

            var searches = await Task.WhenAll(searchable.Select(config => SearchIndexerAsync(config, text, input, policy, cancellationToken)));
            await RecordHealthAsync(searches, cancellationToken);

            foreach (var search in searches) {
                foreach (var release in search.Found) {
                    releases.Add((release, search.Config.Id, search.Config.DisplayName));
                }

                if (search.Error is not null) {
                    errors.TryAdd(
                        search.Config.Id,
                        new IndexerSearchError(search.Config.Id, search.Config.DisplayName, search.Error));
                    // A real failure is not repeated for every broader query in the same operation.
                    // Rate-limit exhaustion likewise cannot recover inside this query ladder.
                    failedIndexers.Add(search.Config.Id);
                }
            }
        }

        var priorityById = configs.ToDictionary(config => config.Id, config => config.Priority);
        var supported = releases
            .Where(candidate => protocols.Contains(candidate.Release.Protocol))
            .ToArray();
        var candidates = engine.Evaluate(supported, rules, blocklisted)
            .GroupBy(candidate => ReleaseIdentity.For(
                candidate.Release.InfoHash,
                candidate.IndexerName,
                candidate.Release.Title), StringComparer.Ordinal)
            // Parse/evaluate before de-duplication: indexers sometimes return the same hash with different
            // titles or metadata. Like Sonarr, keep the copy with the fewest rejections first, then apply
            // indexer priority; a malformed report from a preferred indexer must not hide an accepted copy.
            .Select(group => group
                .OrderByDescending(candidate => candidate.Accepted)
                .ThenBy(candidate => candidate.Rejections.Count)
                .ThenBy(candidate => candidate.IndexerConfigId is { } id
                    ? priorityById.GetValueOrDefault(id, int.MaxValue)
                    : int.MaxValue)
                .ThenByDescending(candidate => candidate.Score)
                .First())
            .ToArray();
        return new AcquisitionSearchOutcome(
            Prioritize(candidates, input.Kind, preferredProtocol, priorityById),
            errors.Values.ToArray());
    }

    /// <summary>
    /// Keeps every supported result visible for manual review while applying the global comparison order.
    /// Job handlers reapply the same quality/protocol/swarm order after persistence.
    /// </summary>
    private static IReadOnlyList<ScoredRelease> Prioritize(
        IReadOnlyList<ScoredRelease> candidates,
        EntityKind kind,
        DownloadProtocol preferredProtocol,
        IReadOnlyDictionary<Guid, int> priorityById) =>
        candidates
            .OrderByDescending(candidate => candidate.Accepted)
            .ThenByDescending(candidate => candidate.Score - AcquisitionReleaseRanking.SwarmTieBreak(
                kind,
                candidate.Release.Protocol,
                candidate.Release.Seeders,
                candidate.Release.Peers))
            .ThenByDescending(candidate => candidate.Release.Protocol == preferredProtocol)
            .ThenByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.IndexerConfigId is { } id
                ? priorityById.GetValueOrDefault(id, int.MaxValue)
                : int.MaxValue)
            .ToArray();

    /// <summary>One indexer's contribution to a search rung. A rate-limited skip carries a message but is not a failure.</summary>
    private sealed record IndexerSearchResult(
        Contracts.Acquisition.IndexerConfigDetail Config,
        IReadOnlyList<IndexerRelease> Found,
        string? Error,
        bool RateLimited = false);

    private async Task<IndexerSearchResult> SearchIndexerAsync(
        Contracts.Acquisition.IndexerConfigDetail config,
        string text,
        AcquisitionSearchInput input,
        IAcquisitionPolicyModule policy,
        CancellationToken cancellationToken) {
        // A rate-limited skip is surfaced (so a thin result set is explainable) but is NOT a failure —
        // it must not climb the backoff ladder.
        if (!queryWindow.TryRecordQuery(config.Id, config.QueryLimitPerHour)) {
            return new IndexerSearchResult(config, [], "Hourly query limit reached; this indexer was skipped for this search.", RateLimited: true);
        }

        try {
            // Narrow the indexer's configured categories to the acquisition kind's Torznab range, so a
            // movie or album search never queries the book categories the indexer was set up with.
            var categories = policy.RouteCategories(input, config.Categories);
            var connection = new IndexerConnection(config.Id, config.Kind, config.BaseUrl, config.ApiKey, categories);
            var found = await clients.Get(config.Kind).SearchAsync(connection, new IndexerQuery(text, categories, input.Kind), cancellationToken);
            return new IndexerSearchResult(config, found, null);
        } catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            // HttpClient reports its own Timeout as TaskCanceledException. That is one indexer's
            // failure, not cancellation of the durable acquisition-search job.
            return new IndexerSearchResult(config, [], ex.Message);
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
