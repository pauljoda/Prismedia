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
    SettingsService settings,
    TvPayloadAdmission? payloadAdmission = null) {
    private static readonly TimeSpan HealthProbeTimeout = TimeSpan.FromSeconds(15);
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

        var protocols = (await downloadClients.GetEnabledProtocolsAsync(cancellationToken)).Distinct().ToArray();
        if (protocols.Length == 0) return new AcquisitionSearchOutcome([], []);
        var rules = AcquisitionRuleContext.Apply(
            await profiles.GetRulesAsync(input.ProfileId, input.Kind, cancellationToken), input, upgradeOwnedQuality,
            (await settings.GetProperDownloadSettingsAsync(cancellationToken)).Policy, protocols);
        var preferredProtocol = await AcquisitionProtocolPreference.ResolveAsync(downloadClients, settings, cancellationToken)
            ?? protocols[0];

        var blocklisted = await blocklist.GetIdentitiesAsync(cancellationToken);
        var excluded = payloadAdmission is null ? new HashSet<string>() : await payloadAdmission.GetExcludedAsync(input, cancellationToken);
        var engine = policy.DecisionEngineFor(input.Kind);
        ScoredRelease ApplyCoverage(ScoredRelease candidate) => excluded.Contains(ReleaseIdentity.For(
            candidate.Release.InfoHash, candidate.IndexerName, candidate.Release.Title))
                ? candidate with { Accepted = false, Rejections = candidate.Rejections.Append(ReleaseRejectionReason.NotAnUpgrade).Distinct().ToArray() }
                : candidate;

        // Every query variant contributes to one decision set. Stopping at the first acceptable rung
        // made indexer query wording decide the winner before quality, formats, protocol, health and
        // priority could be compared. Arr-style search instead aggregates and de-duplicates the full
        // applicable set, then makes one global decision.
        var releases = new List<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)>();
        var errors = new Dictionary<Guid, IndexerSearchError>();
        var failedIndexers = new HashSet<Guid>();
        async Task SearchQueriesAsync(IEnumerable<string> searchQueries) {
            foreach (var text in searchQueries) {
                var searchable = configs.Where(config => !failedIndexers.Contains(config.Id)).ToArray();
                if (searchable.Length == 0) {
                    break;
                }

                var searches = await Task.WhenAll(searchable.Select(config => SearchIndexerAsync(config, text, input, policy, protocols, cancellationToken)));
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
        }

        await SearchQueriesAsync(queries);

        // Episode-title queries are a fallback, not another unconditional rung. Evaluate the complete
        // exact-unit result set first; only when every candidate is rejected do we spend the extra query
        // budget on title-only releases. Reviewed custom searches remain exactly the term the user chose.
        if (string.IsNullOrWhiteSpace(customQuery)) {
            var fallbackQueries = policy.BuildFallbackQueries(input)
                .Except(queries, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var hasAcceptedPrimary = engine.Evaluate(
                releases.Where(candidate => protocols.Contains(candidate.Release.Protocol)).ToArray(),
                rules,
                blocklisted).Select(ApplyCoverage).Any(candidate => candidate.Accepted);
            if (!hasAcceptedPrimary && fallbackQueries.Length > 0) {
                await SearchQueriesAsync(fallbackQueries);
            }
        }

        var priorityById = configs.ToDictionary(config => config.Id, config => config.Priority);
        var supported = releases
            .Where(candidate => protocols.Contains(candidate.Release.Protocol))
            .ToArray();
        var candidates = engine.Evaluate(supported, rules, blocklisted)
            .Select(ApplyCoverage)
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
        bool RateLimited = false,
        bool ConfirmedHealthy = false);

    private async Task<IndexerSearchResult> SearchIndexerAsync(
        Contracts.Acquisition.IndexerConfigDetail config,
        string text,
        AcquisitionSearchInput input,
        IAcquisitionPolicyModule policy,
        IReadOnlyList<DownloadProtocol> protocols,
        CancellationToken cancellationToken) {
        // A rate-limited skip is surfaced (so a thin result set is explainable) but is NOT a failure —
        // it must not climb the backoff ladder.
        if (!queryWindow.TryRecordQuery(config.Id, config.QueryLimitPerHour)) {
            return new IndexerSearchResult(config, [], "Hourly query limit reached; this indexer was skipped for this search.", RateLimited: true);
        }

        var client = clients.Get(config.Kind);
        var connection = new IndexerConnection(config.Id, config.Kind, config.BaseUrl, config.ApiKey, policy.RouteCategories(input, config.Categories));
        try {
            // Narrow the indexer's configured categories to the acquisition kind's Torznab range, so a
            // movie or album search never queries the book categories the indexer was set up with.
            var categories = connection.Categories;
            var found = await client.SearchAsync(connection, new IndexerQuery(text, categories, input.Kind) { Protocols = protocols }, cancellationToken);
            return new IndexerSearchResult(config, found, null);
        } catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            // HttpClient reports its own Timeout as TaskCanceledException. That is one indexer's
            // failed query, not cancellation of the durable acquisition-search job. A quick successful
            // health probe proves the provider is still usable, so this slow query must not quarantine it.
            return new IndexerSearchResult(
                config,
                [],
                ex.Message,
                ConfirmedHealthy: await IsHealthyAsync(client, connection, cancellationToken));
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new IndexerSearchResult(
                config,
                [],
                ex.Message,
                ConfirmedHealthy: await IsHealthyAsync(client, connection, cancellationToken));
        }
    }

    private static async Task<bool> IsHealthyAsync(
        IIndexerSearchClient client,
        IndexerConnection connection,
        CancellationToken cancellationToken) {
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCancellation.CancelAfter(HealthProbeTimeout);
        try {
            return (await client.TestAsync(connection, probeCancellation.Token)).Connected;
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return false;
        } catch (Exception exception) when (exception is not OperationCanceledException) {
            return false;
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

            if (search.ConfirmedHealthy) {
                await indexerStatuses.ClearAsync(search.Config.Id, cancellationToken);
            } else if (search.Error is null) {
                await indexerStatuses.RecordSuccessAsync(search.Config.Id, cancellationToken);
            } else {
                await indexerStatuses.RecordFailureAsync(search.Config.Id, search.Error, cancellationToken);
            }
        }
    }
}
