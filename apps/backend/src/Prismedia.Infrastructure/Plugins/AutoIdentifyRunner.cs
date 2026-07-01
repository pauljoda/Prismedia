using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Plugins;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Runs auto identify for a single scanned entity: it walks the user's ordered provider list and
/// applies the first proposal that clears the configured confidence bar (or is an exact match),
/// applying provider metadata — scalar fields, structural children, relationships, and artwork —
/// while leaving user fields such as rating untouched. Successfully identified entities are marked
/// organized so the un-organized-only gate skips them on later scans.
/// </summary>
public sealed class AutoIdentifyRunner(
    SettingsService settings,
    IIdentifyProviderService identify,
    PrismediaDbContext db,
    ILogger<AutoIdentifyRunner> logger,
    AutoIdentifyConcurrencyGate? gate = null) : IAutoIdentifyRunner {
    public Task<AutoIdentifyResult> RunAsync(Guid entityId, CancellationToken cancellationToken) =>
        RunAsync(entityId, AutoIdentifyRunOptions.Default, cancellationToken);

    public async Task<AutoIdentifyResult> RunAsync(
        Guid entityId,
        AutoIdentifyRunOptions options,
        CancellationToken cancellationToken) {
        var config = await settings.GetAutoIdentifySettingsAsync(cancellationToken);
        if (!config.Enabled) {
            return new AutoIdentifyResult(false, SkipReason: "auto identify disabled");
        }

        if (config.Providers.Count == 0) {
            return new AutoIdentifyResult(false, SkipReason: "no providers configured");
        }

        var entity = await db.Entities
            .FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
        if (entity is null) {
            return new AutoIdentifyResult(false, SkipReason: "entity not found");
        }

        // Only identify scan roots. A child (an episode in a series, an image in a gallery,
        // a track in an album) is filled by cascading from its identified parent, so identifying it
        // directly would duplicate and conflict with the parent's work. Audio albums are the exception:
        // Artist/Album scans intentionally parent albums under a MusicArtist grouping, but the album is
        // still the metadata root to identify and cascade to tracks.
        if (entity.ParentEntityId is not null && entity.KindCode != EntityKindRegistry.AudioLibrary.Code) {
            return new AutoIdentifyResult(false, SkipReason: "child entity; its parent is identified instead");
        }

        if (!AutoIdentifySelectorKinds.TryMap(entity.KindCode, out var selectorKind)) {
            return new AutoIdentifyResult(false, SkipReason: $"kind '{entity.KindCode}' is not auto-identifiable");
        }

        var selectedKinds = config.EntityKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!selectedKinds.Contains(selectorKind)) {
            return new AutoIdentifyResult(false, SkipReason: $"kind '{selectorKind}' not selected");
        }

        if (config.UnorganizedOnly && entity.IsOrganized) {
            return new AutoIdentifyResult(false, SkipReason: "already organized");
        }

        if (entity.AutoIdentifyAttempts >= AutoIdentifyPolicy.MaxAttemptsPerEntity) {
            return new AutoIdentifyResult(false, SkipReason: "auto identify attempts exhausted; identify manually");
        }

        // Restrict to user-selected providers that are installed, enabled, and capable of this kind,
        // preserving the user's configured priority order. Capability is checked against the entity's
        // concrete kind code (e.g. audio-library), not the settings selector (e.g. audio) — provider
        // manifests declare concrete kinds, and the identify call itself gates on the concrete kind.
        var capable = (await identify.ListProvidersAsync(entity.KindCode, cancellationToken))
            .Where(provider => provider.Installed && provider.Enabled)
            .Select(provider => provider.Id)
            .ToHashSet(StringComparer.Ordinal);
        var providerIds = config.Providers
            .Where(capable.Contains)
            .ToArray();
        if (providerIds.Length == 0) {
            return new AutoIdentifyResult(false, SkipReason: "no capable provider");
        }

        var parentExternalIds = await LoadParentExternalIdsForContextAsync(
            entity.KindCode,
            entity.ParentEntityId,
            cancellationToken);

        using var lease = gate?.TryEnterBackground()
            ?? (gate is null ? null : throw new JobRetryLaterException("Auto identify provider slot busy.", TimeSpan.FromSeconds(5)));
        using var inactivity = ProgressSensitiveCancellation.Create(options.InactivityTimeout, cancellationToken);
        var runToken = inactivity?.Token ?? cancellationToken;
        var progressSink = AutoIdentifyProgressSink.Create(options, inactivity);

        // An artist grouping identifies for its own metadata and artwork only. Its albums are
        // independent auto-identify roots, so cascading the artist into them would duplicate and
        // race that per-album work.
        var cascadeChildren = entity.KindCode != EntityKindRegistry.MusicArtist.Code;

        foreach (var providerId in providerIds) {
            runToken.ThrowIfCancellationRequested();

            IdentifyPluginResponse response;
            try {
                response = await identify.IdentifyAsync(
                    entityId,
                    providerId,
                    query: null,
                    parentExternalIds,
                    hideNsfw: false,
                    runToken,
                    cascadeChildren,
                    progressSink);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "AutoIdentify: provider {Provider} failed for entity {EntityId}", providerId, entityId);
                continue;
            }

            if (!response.Ok || response.Result is null) {
                if (ProviderTransientErrors.IsRetryable(response.Error)) {
                    throw new JobRetryLaterException(
                        $"Auto identify provider {providerId} is temporarily unavailable: {response.Error}",
                        TimeSpan.FromMinutes(1));
                }

                continue;
            }

            var proposal = response.Result;
            if (proposal.Patch is null && proposal.Candidates is { Count: > 0 } candidates) {
                var candidate = SelectConfidentCandidate(candidates, config.ConfidenceThreshold, entity.Title);
                if (candidate is null) {
                    continue;
                }

                try {
                    response = await identify.IdentifyAsync(
                        entityId,
                        providerId,
                        new IdentifyQuery(Title: null, Url: null, ExternalIds: candidate.ExternalIds),
                        parentExternalIds,
                        hideNsfw: false,
                        runToken,
                        cascadeChildren,
                        progressSink);
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception ex) {
                    logger.LogWarning(ex, "AutoIdentify: provider {Provider} failed to hydrate candidate for entity {EntityId}", providerId, entityId);
                    continue;
                }

                if (!response.Ok || response.Result?.Patch is null) {
                    if (ProviderTransientErrors.IsRetryable(response.Error)) {
                        throw new JobRetryLaterException(
                            $"Auto identify provider {providerId} is temporarily unavailable: {response.Error}",
                            TimeSpan.FromMinutes(1));
                    }

                    continue;
                }

                proposal = response.Result;
            }

            if (!MeetsConfidenceBar(proposal, config.ConfidenceThreshold)) {
                continue;
            }

            var fields = ProposalApplySelection.SelectAllPresentFields(proposal);
            var images = ProposalApplySelection.SelectDefaultImages(proposal);
            var acceptedProposal = AcceptedProposalMarker.MarkTreeOrganized(proposal);
            inactivity?.Reset();
            var applied = await identify.ApplyAsync(entityId, acceptedProposal, fields, images, runToken, progressSink);
            if (!applied) {
                continue;
            }

            inactivity?.Reset();
            await MarkOrganizedAsync(entityId, runToken);
            return new AutoIdentifyResult(true, providerId, proposal.Confidence);
        }

        // A completed run that queried providers and applied nothing consumes one of the entity's
        // attempts; at the policy maximum the entity is left for manual identify. Transient
        // provider outages retry the job via JobRetryLaterException and never reach this point.
        entity.AutoIdentifyAttempts += 1;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(runToken);

        var remaining = AutoIdentifyPolicy.MaxAttemptsPerEntity - entity.AutoIdentifyAttempts;
        return new AutoIdentifyResult(false, SkipReason: remaining > 0
            ? $"no confident match ({remaining} auto attempt{(remaining == 1 ? "" : "s")} left)"
            : "no confident match; auto identify attempts exhausted, identify manually");
    }

    /// <summary>
    /// Loads external IDs from an already-identified parent artist so album auto-identify can resolve
    /// inside the artist context instead of searching every matching album title globally.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>?> LoadParentExternalIdsForContextAsync(
        string kindCode,
        Guid? parentEntityId,
        CancellationToken cancellationToken) {
        // Artist-parented albums are auto-identify roots, but the provider should still be able to
        // constrain album lookup by the already-identified artist MBID/IDs when available.
        if (parentEntityId is not { } parentId || kindCode != EntityKindRegistry.AudioLibrary.Code) {
            return null;
        }

        var rows = await db.EntityExternalIds.AsNoTracking()
            .Where(externalId => externalId.EntityId == parentId)
            .Select(externalId => new { externalId.Provider, externalId.Value })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) {
            return null;
        }

        return rows
            .GroupBy(row => row.Provider, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Picks a single provider-ranked search candidate only when the provider supplied an explicit
    /// confidence score that meets the user's auto-apply threshold.
    /// </summary>
    private static EntitySearchCandidate? SelectConfidentCandidate(
        IReadOnlyList<EntitySearchCandidate> candidates,
        double threshold,
        string queryTitle) {
        var ranked = candidates
            .Where(candidate => candidate.ExternalIds.Count > 0)
            .Select(candidate => new {
                Candidate = candidate,
                Confidence = CandidateConfidence(candidate, queryTitle)
            })
            .Where(row => row.Confidence is not null)
            .OrderByDescending(row => row.Confidence!.Value)
            .ToArray();

        if (ranked.Length == 0 || ranked[0].Confidence!.Value < threshold) {
            return null;
        }

        if (ranked.Length > 1 && ranked[1].Confidence!.Value >= threshold) {
            return null;
        }

        return ranked[0].Candidate;
    }

    private static double? CandidateConfidence(EntitySearchCandidate candidate, string queryTitle) {
        if (candidate.Confidence is { } confidence) {
            return NormalizeConfidence(confidence);
        }

        return ExactTitleCandidateConfidence(candidate, queryTitle);
    }

    private static double? ExactTitleCandidateConfidence(EntitySearchCandidate candidate, string queryTitle) {
        var query = NormalizeCandidateTitle(queryTitle);
        var candidateTitle = NormalizeCandidateTitle(candidate.Title);
        if (query.TitleKey.Length == 0 || candidateTitle.TitleKey.Length == 0 ||
            !string.Equals(query.TitleKey, candidateTitle.TitleKey, StringComparison.Ordinal)) {
            return null;
        }

        var candidateYear = candidate.Year ?? candidateTitle.Year;
        if (query.Year is { } queryYear && candidateYear is { } year && year != queryYear) {
            return null;
        }

        return 1d;
    }

    private static (string TitleKey, int? Year) NormalizeCandidateTitle(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return (string.Empty, null);
        }

        var title = value.Trim();
        int? year = null;
        if (TryStripTrailingYear(title, out var stripped, out var parsedYear)) {
            title = stripped;
            year = parsedYear;
        }

        var tokens = title.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()))
            .Where(token => token.Length > 0);
        return (string.Join(' ', tokens), year);
    }

    private static bool TryStripTrailingYear(string title, out string stripped, out int year) {
        stripped = title;
        year = 0;
        if (title.Length < 6) {
            return false;
        }

        var close = title[^1];
        var open = close switch {
            ')' => '(',
            ']' => '[',
            _ => '\0'
        };
        if (open == '\0' || title[^6] != open) {
            return false;
        }

        var yearText = title.Substring(title.Length - 5, 4);
        if (!yearText.All(char.IsDigit) || !int.TryParse(yearText, out year)) {
            return false;
        }

        stripped = title[..^6].TrimEnd();
        return stripped.Length > 0;
    }

    /// <summary>
    /// A proposal qualifies for auto-apply when it carries concrete metadata and either reports a
    /// confidence at or above the threshold or reports no confidence at all (treated as an exact /
    /// definitive match, as with id/url lookups and deterministic scrapers).
    /// </summary>
    private static bool MeetsConfidenceBar(EntityMetadataProposal proposal, double threshold) {
        var patch = proposal.Patch;
        // A series root often carries little of its own metadata; its matched children are the value,
        // so treat structural children as concrete content too. Collections may be null on synthetic
        // structural roots, so guard every access.
        var hasConcreteMetadata =
            !string.IsNullOrWhiteSpace(patch?.Title) ||
            patch?.ExternalIds is { Count: > 0 } ||
            proposal.Children is { Count: > 0 };
        if (!hasConcreteMetadata) {
            return false;
        }

        if (proposal.Confidence is not { } confidence) {
            return true;
        }

        return NormalizeConfidence(confidence) >= threshold;
    }

    private static double NormalizeConfidence(decimal confidence) {
        // Confidence is a 0-1 fraction by contract; tolerate providers that report a 0-100 percentage.
        var normalized = (double)confidence;
        return normalized > 1d ? normalized / 100d : normalized;
    }

    private sealed class ProgressSensitiveCancellation : IDisposable {
        private readonly CancellationTokenSource _source;
        private readonly TimeSpan _timeout;

        private ProgressSensitiveCancellation(TimeSpan timeout, CancellationToken cancellationToken) {
            _timeout = timeout;
            _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Reset();
        }

        public CancellationToken Token => _source.Token;

        public static ProgressSensitiveCancellation? Create(TimeSpan? timeout, CancellationToken cancellationToken) =>
            timeout is { } value && value > TimeSpan.Zero
                ? new ProgressSensitiveCancellation(value, cancellationToken)
                : null;

        public void Reset() {
            if (!_source.IsCancellationRequested) {
                _source.CancelAfter(_timeout);
            }
        }

        public void Dispose() => _source.Dispose();
    }

    private sealed class AutoIdentifyProgressSink(
        AutoIdentifyRunOptions options,
        ProgressSensitiveCancellation? timeout) : IIdentifyCascadeSink, IIdentifyApplyProgressReporter {
        private int _resolvedSteps;
        private int _rootChildCount;

        public static AutoIdentifyProgressSink? Create(
            AutoIdentifyRunOptions options,
            ProgressSensitiveCancellation? timeout) =>
            options.ReportProgressAsync is not null || timeout is not null
                ? new AutoIdentifyProgressSink(options, timeout)
                : null;

        public async Task OnEntityResolvedAsync(EntityMetadataProposal partialRoot, CancellationToken cancellationToken) {
            Interlocked.Exchange(ref _rootChildCount, EntityMetadataProposalTraversal.StructuralChildren(partialRoot).Count);
            await ReportProgressAsync(cancellationToken, AutoIdentifyProgressPhase.Identifying);
        }

        public Task OnProgressAsync(CancellationToken cancellationToken) =>
            ReportProgressAsync(cancellationToken, AutoIdentifyProgressPhase.Identifying);

        public Task ReportEntityAsync(
            EntityKind kind,
            string title,
            IReadOnlyList<string> path,
            CancellationToken cancellationToken) =>
            ReportProgressAsync(cancellationToken, AutoIdentifyProgressPhase.Applying, title, path);

        private async Task ReportProgressAsync(
            CancellationToken cancellationToken,
            AutoIdentifyProgressPhase phase,
            string? currentTitle = null,
            IReadOnlyList<string>? currentPath = null) {
            timeout?.Reset();
            var steps = Interlocked.Increment(ref _resolvedSteps);
            if (options.ReportProgressAsync is null) {
                return;
            }

            await options.ReportProgressAsync(
                new AutoIdentifyProgress(
                    phase,
                    steps,
                    Volatile.Read(ref _rootChildCount),
                    currentTitle,
                    currentPath),
                cancellationToken);
        }

        public Task<bool> IsActiveAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private async Task MarkOrganizedAsync(Guid entityId, CancellationToken cancellationToken) {
        var entity = await db.Entities.FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
        if (entity is null || entity.IsOrganized) {
            return;
        }

        entity.IsOrganized = true;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
