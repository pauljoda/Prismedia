using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Owns the durable identify queue state machine for entity metadata review.
/// </summary>
public sealed class IdentifyQueueService : IIdentifyQueueService {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        // Persisted proposal/query JSON carries codec enums (e.g. proposal TargetKind) as their
        // stable string code, so the stored column matches the HTTP/plugin wire format.
        Converters = { new CodecJsonConverterFactory() }
    };

    private readonly PrismediaDbContext _db;
    private readonly IdentifyPluginService _identify;
    private readonly IIdentifyApplyProgressStore _progress;
    private readonly IJobQueueService _jobs;
    private readonly IIdentifyTargetEligibilityService _eligibility;

    public IdentifyQueueService(
        PrismediaDbContext db,
        IdentifyPluginService identify,
        IIdentifyApplyProgressStore progress,
        IJobQueueService jobs,
        IIdentifyTargetEligibilityService eligibility) {
        _db = db;
        _identify = identify;
        _progress = progress;
        _jobs = jobs;
        _eligibility = eligibility;
    }

    /// <summary>
    /// Lists active identify queue items, optionally including terminal history rows.
    /// </summary>
    public async Task<IReadOnlyList<IdentifyQueueItem>> ListAsync(
        bool includeCompleted,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var query = _db.IdentifyQueueItems.AsNoTracking();
        if (!includeCompleted) {
            query = query.Where(row => row.State != IdentifyQueueState.Done && row.State != IdentifyQueueState.Deleted);
        }
        if (hideNsfw) {
            query = query.Where(row => !_db.Entities.Any(entity => entity.Id == row.EntityId && entity.IsNsfw));
        }

        var rows = await query
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);

        await ReconcileIneligibleTargetsAsync(rows, cancellationToken);
        await ReconcileOrphanedSearchesAsync(rows, cancellationToken);
        var visibleRows = includeCompleted
            ? rows
            : rows.Where(row => row.State != IdentifyQueueState.Done && row.State != IdentifyQueueState.Deleted).ToArray();
        return await MapRowsAsync(visibleRows, cancellationToken);
    }

    /// <summary>
    /// Gets the queue item for an entity, or null when the entity is not queued.
    /// </summary>
    public async Task<IdentifyQueueItem?> GetAsync(Guid entityId, CancellationToken cancellationToken) {
        var row = await _db.IdentifyQueueItems
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);
        if (row is null) {
            return null;
        }

        await ReconcileIneligibleTargetsAsync([row], cancellationToken);
        await ReconcileOrphanedSearchesAsync([row], cancellationToken);
        return await MapRowAsync(row, cancellationToken);
    }

    /// <summary>
    /// Retires active queue rows whose entity became Wanted or lost its Source binding after it was
    /// queued. This is the queue-side race boundary for file deletion and stale scan cleanup.
    /// </summary>
    private async Task ReconcileIneligibleTargetsAsync(
        IReadOnlyList<IdentifyQueueItemRow> rows,
        CancellationToken cancellationToken) {
        var active = rows
            .Where(row => row.State is not (IdentifyQueueState.Done or IdentifyQueueState.Deleted))
            .ToArray();
        if (active.Length == 0) {
            return;
        }

        var eligibility = await _eligibility.EvaluateManyAsync(
            active.Select(row => row.EntityId).ToArray(),
            cancellationToken);
        var changed = false;
        foreach (var snapshot in active) {
            if (eligibility[snapshot.EntityId].IsEligible) {
                continue;
            }

            var tracked = await _db.IdentifyQueueItems
                .FirstOrDefaultAsync(item => item.Id == snapshot.Id, cancellationToken);
            if (tracked is null || tracked.State is IdentifyQueueState.Done or IdentifyQueueState.Deleted) {
                continue;
            }

            await RetireRowAsync(tracked, cancellationToken);
            snapshot.State = tracked.State;
            snapshot.SearchJobId = null;
            snapshot.CascadeJobId = null;
            snapshot.Error = null;
            snapshot.UpdatedAt = tracked.UpdatedAt;
            snapshot.CompletedAt = tracked.CompletedAt;
            changed = true;
        }

        if (changed) {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Flips queued/searching rows whose identify-search job is gone or terminal into the error
    /// state, so the UI never waits on a search that will not run (a job cancelled from the jobs
    /// page, a worker death past stale-lease recovery, or rows backfilled by migration). The detached
    /// snapshots are updated in place so the caller maps the reconciled state.
    /// </summary>
    private async Task ReconcileOrphanedSearchesAsync(
        IReadOnlyList<IdentifyQueueItemRow> rows,
        CancellationToken cancellationToken) {
        var changed = false;
        foreach (var row in rows) {
            if (row.State is not (IdentifyQueueState.Queued or IdentifyQueueState.Searching)) {
                continue;
            }

            if (row.SearchJobId is not null &&
                await _jobs.HasPendingAsync(JobType.IdentifySearch, row.EntityId.ToString(), cancellationToken)) {
                continue;
            }

            var tracked = await _db.IdentifyQueueItems
                .FirstOrDefaultAsync(item => item.Id == row.Id, cancellationToken);
            if (tracked is null ||
                tracked.State is not (IdentifyQueueState.Queued or IdentifyQueueState.Searching)) {
                continue;
            }

            FinishOwnedSearch(tracked, IdentifyQueueState.Error, "The queued search is no longer running. Search again.");
            row.State = tracked.State;
            row.Error = tracked.Error;
            row.SearchJobId = null;
            row.UpdatedAt = tracked.UpdatedAt;
            changed = true;
        }

        if (changed) {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Adds an entity to the identify queue, preserving active work and resetting terminal items.
    /// </summary>
    public async Task<IdentifyQueueItem> AddAsync(Guid entityId, CancellationToken cancellationToken) {
        (await _eligibility.EvaluateAsync(entityId, cancellationToken)).EnsureEligible();
        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        var now = DateTimeOffset.UtcNow;
        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);

        if (row is null) {
            row = new IdentifyQueueItemRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                State = IdentifyQueueState.Search,
                Action = IdentifyAction.Search,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.IdentifyQueueItems.Add(row);
        } else if (row.State is IdentifyQueueState.Done or IdentifyQueueState.Deleted) {
            ResetForSearch(row, now);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return MapRow(row, entity);
    }

    /// <summary>
    /// Requests a provider search for the entity: the item enters the <see cref="IdentifyQueueState.Queued"/>
    /// state and a background identify-search job runs the actual provider work. Any cascade or search
    /// job still in flight for the item is superseded (cancelled and its ownership marker restamped),
    /// so the newest request always owns the item's next result.
    /// </summary>
    public async Task<IdentifyQueueItem> RequestSearchAsync(
        Guid entityId,
        IdentifyQueueSearchRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);

        (await _eligibility.EvaluateAsync(entityId, cancellationToken)).EnsureEligible();

        var row = await EnsureMutableRowAsync(entityId, cancellationToken);
        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");

        await StampQueuedSearchAsync(row, entity, request, hideNsfw, isForeground: true, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return MapRow(row, entity);
    }

    /// <summary>
    /// Requests provider searches for a batch of entities, one identify-search job per entity.
    /// Entities that no longer exist are skipped.
    /// </summary>
    public async Task<IdentifyBulkAcceptedResponse> RequestSearchBatchAsync(
        IReadOnlyList<Guid> entityIds,
        IdentifyQueueSearchRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(entityIds);
        ArgumentNullException.ThrowIfNull(request);

        var enqueued = 0;
        var distinctIds = entityIds.Distinct().ToArray();
        var eligibility = await _eligibility.EvaluateManyAsync(distinctIds, cancellationToken);
        foreach (var entityId in distinctIds) {
            if (!eligibility[entityId].IsEligible) {
                continue;
            }

            try {
                var row = await EnsureMutableRowAsync(entityId, cancellationToken);
                var entity = await LoadEntityAsync(entityId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");

                await StampQueuedSearchAsync(row, entity, request, hideNsfw, isForeground: false, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                enqueued++;
            } catch (KeyNotFoundException) {
                // The entity was removed since the user selected it; skip it.
            }
        }

        return new IdentifyBulkAcceptedResponse(entityIds.Count, enqueued);
    }

    /// <summary>
    /// Resolves a selected candidate into a proposal without restamping the queue row as a new
    /// background search. The existing candidate list remains visible until the provider returns a
    /// proposal, so a slow or failed ID lookup does not collapse the review back to queued/searching.
    /// </summary>
    public async Task<IdentifyQueueItem> ResolveCandidateAsync(
        Guid entityId,
        IdentifyQueueCandidateRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidate);

        (await _eligibility.EvaluateAsync(entityId, cancellationToken)).EnsureEligible();

        var provider = request.Provider.Trim();
        if (string.IsNullOrWhiteSpace(provider)) {
            throw new ArgumentException("A provider is required to resolve a selected identify candidate.", nameof(request));
        }

        if (request.Candidate.ExternalIds is null) {
            throw new ArgumentException("Selected identify candidate has no provider IDs to resolve.", nameof(request));
        }

        var externalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in request.Candidate.ExternalIds) {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            externalIds[key] = value;
        }

        if (externalIds.Count == 0) {
            throw new ArgumentException("Selected identify candidate has no provider IDs to resolve.", nameof(request));
        }

        if (!externalIds.ContainsKey(provider)) {
            throw new ArgumentException(
                $"Selected identify candidate is missing a provider ID for '{provider}'.",
                nameof(request));
        }

        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Identify queue item for entity '{entityId}' was not found.");
        if (row.State != IdentifyQueueState.Search || string.IsNullOrWhiteSpace(row.CandidatesJson)) {
            throw new InvalidOperationException("Only an identify queue item with candidate results can resolve a selected candidate.");
        }

        var originalUpdatedAt = row.UpdatedAt;
        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        var query = new IdentifyQuery(Title: null, Url: null, ExternalIds: externalIds);

        var response = await _identify.IdentifyAsync(
            entity.Id,
            provider,
            query,
            parentExternalIds: null,
            hideNsfw,
            cancellationToken,
            cascadeChildren: false,
            hydrateRelationships: false);
        if (!response.Ok) {
            throw new InvalidOperationException(response.Error ?? "Selected identify candidate could not be resolved.");
        }

        var proposal = response.Result;
        if (proposal?.Patch is null) {
            throw new InvalidOperationException(response.Error ?? "Selected identify candidate did not return provider metadata.");
        }

        await _db.Entry(row).ReloadAsync(cancellationToken);
        if (row.State != IdentifyQueueState.Search || row.UpdatedAt != originalUpdatedAt) {
            throw new InvalidOperationException("Identify queue item changed while the selected candidate was resolving. Review the latest result and try again.");
        }

        await CancelCascadeAsync(row, cancellationToken);
        await CancelSearchJobAsync(row, cancellationToken);

        row.State = IdentifyQueueState.Proposal;
        row.ProviderCode = provider;
        row.Action = IdentifyAction.LookupId;
        row.QueryJson = JsonSerializer.Serialize(query, JsonOptions);
        row.CandidatesJson = null;
        row.ProposalJson = JsonSerializer.Serialize(proposal, JsonOptions);
        row.Error = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.CompletedAt = null;

        await EnqueueCascadeIfNeededAsync(row, entity, provider, query, proposal, hideNsfw, isForeground: true, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return MapRow(row, entity);
    }

    /// <summary>
    /// Moves the row into <see cref="IdentifyQueueState.Queued"/> for a fresh search request: cancels
    /// any in-flight cascade and search job, clears prior results, persists the provider hint and
    /// query, enqueues the identify-search job, and stamps its id as the row's search owner.
    /// </summary>
    private async Task StampQueuedSearchAsync(
        IdentifyQueueItemRow row,
        EntityRow entity,
        IdentifyQueueSearchRequest request,
        bool hideNsfw,
        bool isForeground,
        CancellationToken cancellationToken) {
        await CancelCascadeAsync(row, cancellationToken);
        await CancelSearchJobAsync(row, cancellationToken);

        row.State = IdentifyQueueState.Queued;
        row.ProviderCode = string.IsNullOrWhiteSpace(request.Provider) ? null : request.Provider;
        row.Action = GuessAction(request.Query);
        row.QueryJson = request.Query is null ? null : JsonSerializer.Serialize(request.Query, JsonOptions);
        row.CandidatesJson = null;
        row.ProposalJson = null;
        row.Error = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.CompletedAt = null;

        var payload = new IdentifySearchPayload(entity.Id, row.ProviderCode, request.Query, hideNsfw, isForeground);
        var job = await _jobs.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.IdentifySearch,
                payload.ToJson(),
                TargetEntityKind: entity.KindCode,
                TargetEntityId: entity.Id.ToString(),
                TargetLabel: entity.Title,
                Priority: JobPriorities.InteractiveIdentify,
                Lane: isForeground ? JobRunLane.ForegroundIdentify : null),
            cancellationToken);
        row.SearchJobId = job.Id;
    }

    /// <summary>
    /// Runs a requested search in the background identify-search job. Walks the requested provider
    /// (or every enabled capable provider when none was requested) until one resolves a proposal or
    /// candidates, persisting <see cref="IdentifyQueueState.Searching"/> per attempt so the UI can
    /// show which provider is being tried. Only writes while the item's search marker still names
    /// <paramref name="searchJobId"/>; a superseded or deleted item is left untouched. Transient
    /// provider failures (rate limits, timeouts) defer the job and drop the item back to
    /// <see cref="IdentifyQueueState.Queued"/> instead of recording a permanent error.
    /// </summary>
    public async Task RunSearchAsync(
        IdentifySearchPayload payload,
        Guid searchJobId,
        bool isFinalAttempt,
        CancellationToken cancellationToken) {
        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == payload.EntityId, cancellationToken);
        if (row is null || row.SearchJobId != searchJobId ||
            row.State is not (IdentifyQueueState.Queued or IdentifyQueueState.Searching)) {
            return;
        }

        if (await RetireIfIneligibleAsync(row, cancellationToken)) {
            return;
        }

        var entity = await LoadEntityAsync(payload.EntityId, cancellationToken);
        if (entity is null) {
            FinishOwnedSearch(row, IdentifyQueueState.Error, "The entity no longer exists.");
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var providers = await ResolveSearchProvidersAsync(payload.Provider, entity, payload.HideNsfw, cancellationToken);
        if (providers.Count == 0) {
            FinishOwnedSearch(row, IdentifyQueueState.Error, $"No enabled provider can identify '{entity.KindCode}'.");
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        try {
            foreach (var provider in providers) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await IsSearchActiveAsync(payload.EntityId, searchJobId, cancellationToken)) {
                    return;
                }

                row.State = IdentifyQueueState.Searching;
                row.ProviderCode = provider;
                row.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);

                var resolved = await SearchProviderWithTitleFallbackAsync(
                    row, entity, provider, payload.Query, payload.HideNsfw, payload.IsForeground, cancellationToken);

                if (row.State == IdentifyQueueState.Error && ProviderTransientErrors.IsRetryable(row.Error)) {
                    // Rate limited or temporarily down: defer the whole job instead of hammering the
                    // next provider, and drop back to queued so the UI chip stays honest.
                    var transientError = row.Error;
                    row.State = IdentifyQueueState.Queued;
                    row.Error = null;
                    row.UpdatedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new JobRetryLaterException(
                        $"Identify provider {provider} is temporarily unavailable: {transientError}",
                        TimeSpan.FromMinutes(1));
                }

                if (resolved) {
                    break;
                }

                // Persist this provider's miss before walking on; the last error stands if all miss.
                await _db.SaveChangesAsync(cancellationToken);
            }

            // Resolved (proposal or candidates) or exhausted with the last error standing — either
            // way the requested search is finished, so release the ownership marker.
            row.SearchJobId = null;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        } catch (OperationCanceledException) {
            // Handler timeout (the job defers) or worker shutdown (the run is recovered later):
            // return to queued so the retry resumes from an honest state. The marker stays ours.
            await TryResetOwnedSearchToQueuedAsync(payload.EntityId, searchJobId);
            throw;
        } catch (JobRetryLaterException) {
            throw;
        } catch (Exception ex) {
            if (isFinalAttempt) {
                await TryFailOwnedSearchAsync(payload.EntityId, searchJobId, ex.Message);
            } else {
                await TryResetOwnedSearchToQueuedAsync(payload.EntityId, searchJobId);
            }

            throw;
        }
    }

    /// <summary>
    /// Searches one provider, retrying once with the entity title when a plain no-query search
    /// errored (ported from the old client-side fallback). Returns true when the row resolved into
    /// a proposal or candidates.
    /// </summary>
    private async Task<bool> SearchProviderWithTitleFallbackAsync(
        IdentifyQueueItemRow row,
        EntityRow entity,
        string provider,
        IdentifyQuery? query,
        bool hideNsfw,
        bool isForeground,
        CancellationToken cancellationToken) {
        await RunProviderSearchAsync(row, entity, provider, query, hideNsfw, isForeground, cancellationToken);

        if (row.State == IdentifyQueueState.Error &&
            !ProviderTransientErrors.IsRetryable(row.Error) &&
            IsPlainSearch(query) &&
            !string.IsNullOrWhiteSpace(entity.Title)) {
            await RunProviderSearchAsync(
                row, entity, provider,
                new IdentifyQuery(entity.Title, Url: null, ExternalIds: null),
                hideNsfw, isForeground, cancellationToken);
        }

        return IsResolvedSearchOutcome(row);
    }

    /// <summary>
    /// Runs one provider identify call and writes the outcome onto the row (without saving): a
    /// hydrated proposal (which also enqueues the child cascade), candidates for user choice, or an
    /// error. Provider exceptions become an error outcome so a crashing provider does not abort a
    /// multi-provider walk.
    /// </summary>
    private async Task RunProviderSearchAsync(
        IdentifyQueueItemRow row,
        EntityRow entity,
        string provider,
        IdentifyQuery? query,
        bool hideNsfw,
        bool isForeground,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        row.ProviderCode = provider;
        row.Action = GuessAction(query);
        row.QueryJson = query is null ? null : JsonSerializer.Serialize(query, JsonOptions);
        row.UpdatedAt = now;
        row.CompletedAt = null;

        // Seed only: identify the entity and bind whatever children the provider returned in its own
        // proposal, but do NOT walk the local child tree here — that runs in the background cascade job
        // so the search stays bounded. The full tree is streamed onto the proposal afterwards.
        IdentifyPluginResponse response;
        try {
            response = await _identify.IdentifyAsync(
                entity.Id,
                provider,
                query,
                parentExternalIds: null,
                hideNsfw,
                cancellationToken,
                cascadeChildren: false,
                hydrateRelationships: false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            response = new IdentifyPluginResponse(false, null, ex.Message);
        }

        var requireChoice = query?.RequireChoice == true;
        if (!response.Ok) {
            row.State = IdentifyQueueState.Error;
            row.Error = response.Error ?? "Identify failed.";
            row.CandidatesJson = null;
            row.ProposalJson = null;
        } else if (requireChoice && response.Result is not null) {
            var candidates = ChoiceCandidates(response.Result, entity);
            if (candidates.Count > 0) {
                row.State = IdentifyQueueState.Search;
                row.Error = null;
                row.CandidatesJson = JsonSerializer.Serialize(candidates, JsonOptions);
                row.ProposalJson = null;
            } else {
                row.State = IdentifyQueueState.Error;
                row.Error = response.Error ?? "No provider match was found.";
                row.CandidatesJson = null;
                row.ProposalJson = null;
            }
        } else if (response.Result?.Patch is not null) {
            row.State = IdentifyQueueState.Proposal;
            row.Error = null;
            row.CandidatesJson = null;
            row.ProposalJson = JsonSerializer.Serialize(response.Result, JsonOptions);
            await EnqueueCascadeIfNeededAsync(
                row, entity, provider, query, response.Result, hideNsfw, isForeground, cancellationToken);
        } else if (response.Result?.Candidates is { Count: > 0 } candidates) {
            row.State = IdentifyQueueState.Search;
            row.Error = null;
            row.CandidatesJson = JsonSerializer.Serialize(candidates, JsonOptions);
            row.ProposalJson = null;
        } else {
            row.State = IdentifyQueueState.Error;
            row.Error = response.Error ?? "No provider match was found.";
            row.CandidatesJson = null;
            row.ProposalJson = null;
        }
    }

    /// <summary>
    /// Resolves which providers a search walks: the explicitly requested one, or every installed,
    /// enabled, credentialed provider capable of the entity's kind, in catalog order. Mirrors the
    /// review screen's provider filter; the auto-identify settings gate deliberately does not apply
    /// to user-requested searches.
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolveSearchProvidersAsync(
        string? requestedProvider,
        EntityRow entity,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(requestedProvider)) {
            return [requestedProvider];
        }

        return (await _identify.ListProvidersAsync(entity.KindCode, cancellationToken))
            .Where(provider => provider.Installed &&
                provider.Enabled &&
                provider.MissingAuthKeys.Count == 0 &&
                (!hideNsfw || !provider.IsNsfw))
            .Select(provider => provider.Id)
            .ToArray();
    }

    /// <summary>Whether the search query carries no user-provided hints (plain search).</summary>
    private static bool IsPlainSearch(IdentifyQuery? query) =>
        string.IsNullOrWhiteSpace(query?.Title) &&
        string.IsNullOrWhiteSpace(query?.Url) &&
        query?.ExternalIds is not { Count: > 0 } &&
        query?.RequireChoice != true;

    /// <summary>Whether the row holds a reviewable search result (proposal or candidates).</summary>
    private static bool IsResolvedSearchOutcome(IdentifyQueueItemRow row) =>
        row.State == IdentifyQueueState.Proposal ||
        (row.State == IdentifyQueueState.Search && row.CandidatesJson is not null);

    /// <summary>Writes a terminal search outcome and releases the row's search ownership marker.</summary>
    private static void FinishOwnedSearch(IdentifyQueueItemRow row, IdentifyQueueState state, string? error) {
        row.State = state;
        row.Error = error;
        row.CandidatesJson = null;
        row.ProposalJson = null;
        row.SearchJobId = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Reports whether the requested search is still live: the row exists, is still awaiting or
    /// running a search, and is still marked with this run's job id. Fresh read so a supersede from
    /// another request is observed mid-walk.
    /// </summary>
    private async Task<bool> IsSearchActiveAsync(Guid entityId, Guid searchJobId, CancellationToken cancellationToken) {
        var row = await _db.IdentifyQueueItems
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);
        return row is { State: IdentifyQueueState.Queued or IdentifyQueueState.Searching }
            && row.SearchJobId == searchJobId;
    }

    /// <summary>Cancels the pending identify-search job for a row, if any, and clears its marker.</summary>
    private async Task CancelSearchJobAsync(IdentifyQueueItemRow row, CancellationToken cancellationToken) {
        if (row.SearchJobId is not { } jobId) {
            return;
        }

        try {
            await _jobs.CancelRunAsync(jobId, cancellationToken);
        } catch {
            // Best-effort: the job may already be terminal.
        }

        row.SearchJobId = null;
    }

    /// <summary>
    /// Best-effort: returns an owned in-flight search to <see cref="IdentifyQueueState.Queued"/> so a
    /// deferred job's retry resumes from an honest state. No-ops when the marker is no longer ours.
    /// </summary>
    private async Task TryResetOwnedSearchToQueuedAsync(Guid entityId, Guid searchJobId) {
        try {
            var row = await _db.IdentifyQueueItems
                .FirstOrDefaultAsync(item => item.EntityId == entityId, CancellationToken.None);
            if (row is null || row.SearchJobId != searchJobId ||
                row.State is not (IdentifyQueueState.Queued or IdentifyQueueState.Searching)) {
                return;
            }

            row.State = IdentifyQueueState.Queued;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        } catch {
            // Best effort: orphan reconciliation repairs the state on the next read.
        }
    }

    /// <summary>
    /// Best-effort: marks an owned search as failed on its final attempt so the item never stays
    /// queued/searching for a job that will not run again.
    /// </summary>
    private async Task TryFailOwnedSearchAsync(Guid entityId, Guid searchJobId, string error) {
        try {
            var row = await _db.IdentifyQueueItems
                .FirstOrDefaultAsync(item => item.EntityId == entityId, CancellationToken.None);
            if (row is null || row.SearchJobId != searchJobId ||
                row.State is not (IdentifyQueueState.Queued or IdentifyQueueState.Searching)) {
                return;
            }

            FinishOwnedSearch(row, IdentifyQueueState.Error, error);
            await _db.SaveChangesAsync(CancellationToken.None);
        } catch {
            // Best effort: orphan reconciliation repairs the state on the next read.
        }
    }

    /// <summary>
    /// Enqueues a background cascade to stream the entity's full child tree and related entity details
    /// onto the seeded proposal when there is deferred work to walk.
    /// </summary>
    private async Task EnqueueCascadeIfNeededAsync(
        IdentifyQueueItemRow row,
        EntityRow entity,
        string provider,
        IdentifyQuery? query,
        EntityMetadataProposal proposal,
        bool hideNsfw,
        bool isForeground,
        CancellationToken cancellationToken) {
        var needsStructuralCascade = false;
        if (EntityKindRegistry.EnumeratesIdentifyChildren(entity.KindCode)) {
            needsStructuralCascade = await _db.Entities
                .AsNoTracking()
                .AnyAsync(child => child.ParentEntityId == entity.Id, cancellationToken);
        }

        var needsRelationshipCascade = await HasHydratableRelationshipsAsync(provider, proposal, cancellationToken);
        if (!needsStructuralCascade && !needsRelationshipCascade) {
            return;
        }

        var gatesApply = needsStructuralCascade;
        var payload = new IdentifyCascadePayload(
            entity.Id,
            provider,
            query,
            hideNsfw,
            isForeground,
            GateApply: gatesApply,
            ExpectedProposalId: gatesApply ? null : proposal.ProposalId);
        var job = await _jobs.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.IdentifyCascade,
                payload.ToJson(),
                TargetEntityKind: entity.KindCode,
                TargetEntityId: entity.Id.ToString(),
                TargetLabel: entity.Title,
                Priority: JobPriorities.InteractiveIdentify,
                Lane: isForeground ? JobRunLane.ForegroundIdentify : null),
            cancellationToken);
        if (gatesApply) {
            row.CascadeJobId = job.Id;
        }
    }

    private async Task<bool> HasHydratableRelationshipsAsync(
        string provider,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        var relationships = EntityMetadataProposalTraversal.Relationships(proposal)
            .Concat((proposal.Children ?? []).Where(child => EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind)))
            .ToArray();
        if (relationships.Length == 0) {
            return false;
        }

        var supportByKind = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in relationships) {
            var patch = relationship.Patch;
            var hasLookupInput =
                (patch.ExternalIds?.Count ?? 0) > 0 ||
                (patch.Urls?.Count ?? 0) > 0 ||
                !string.IsNullOrWhiteSpace(patch.Title);
            if (!hasLookupInput) {
                continue;
            }

            var kindCode = relationship.TargetKind.ToEntityKind().ToCode();
            if (!supportByKind.TryGetValue(kindCode, out var supportsKind)) {
                var providers = await _identify.ListProvidersAsync(kindCode, cancellationToken);
                supportsKind = providers.Any(candidate =>
                    candidate.Id.Equals(provider, StringComparison.OrdinalIgnoreCase) && candidate.Enabled);
                supportByKind[kindCode] = supportsKind;
            }

            if (supportsKind) {
                return true;
            }
        }

        return false;
    }

    /// <summary>Cancels the in-flight cascade job for a row, if any, and clears its marker.</summary>
    private async Task CancelCascadeAsync(IdentifyQueueItemRow row, CancellationToken cancellationToken) {
        if (row.CascadeJobId is not { } jobId) {
            return;
        }

        try {
            await _jobs.CancelRunAsync(jobId, cancellationToken);
        } catch {
            // Best-effort: the job may already be terminal.
        }

        row.CascadeJobId = null;
    }

    /// <summary>
    /// Clears the cascade marker for an entity once this cascade's background walk finishes. Only clears
    /// when the marker still names <paramref name="cascadeJobId"/>, so a cascade that was superseded by a
    /// newer search (which already stamped its own job id) does not wipe the newer run's marker.
    /// </summary>
    public async Task ClearCascadeJobAsync(Guid entityId, Guid cascadeJobId, CancellationToken cancellationToken) {
        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);
        if (row?.CascadeJobId != cascadeJobId) {
            return;
        }

        row.CascadeJobId = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Runs the background full-tree cascade for a queued entity, streaming the growing proposal onto
    /// the queue item as each child resolves. Clears the item's cascade marker on success or on the
    /// final failed attempt, but keeps it set across retryable failures so the review screen stays
    /// gated while the job is still going to retry.
    /// </summary>
    public async Task RunCascadeAsync(IdentifyCascadePayload payload, Guid cascadeJobId, bool isFinalAttempt, CancellationToken cancellationToken) {
        try {
            if (await RetireActiveTargetIfIneligibleAsync(payload.EntityId, cancellationToken)) {
                return;
            }

            var sink = new QueueProposalSink(
                this,
                payload.EntityId,
                cascadeJobId,
                payload.GateApply,
                payload.ExpectedProposalId);
            if (!await sink.IsActiveAsync(cancellationToken)) {
                return;
            }

            var response = await _identify.IdentifyAsync(
                payload.EntityId,
                payload.Provider,
                payload.Query,
                parentExternalIds: null,
                payload.HideNsfw,
                cancellationToken,
                cascadeChildren: true,
                sink: sink);

            // Persist the final tree (the last stream flush already carries it, but make the terminal
            // state explicit and resilient to a 0-child cascade that never flushed) — but only while the
            // item is still queued and still ours, so a removed or superseded item is never revived.
            if (response.Ok && response.Result?.Patch is not null
                && await IsCascadeActiveAsync(
                    payload.EntityId,
                    cascadeJobId,
                    payload.GateApply,
                    payload.ExpectedProposalId,
                    cancellationToken)) {
                await SaveProposalSafelyAsync(payload.EntityId, response.Result, cancellationToken);
            }

            // The cascade finished (with a tree, or with no further matches): clear the marker so the
            // review screen's Accept unlocks. Id-guarded, so a superseding search's newer marker survives.
            if (payload.GateApply) {
                await ClearCascadeJobAsync(payload.EntityId, cascadeJobId, CancellationToken.None);
            }
        } catch (OperationCanceledException) {
            // Superseded/deleted (CancelCascadeAsync already replaced or cleared our marker, so a clear
            // here would be a no-op) or worker shutdown (the run is recovered and re-attempted later).
            // Either way, leave the marker untouched and let the supersede/retry path own it.
            throw;
        } catch {
            // A retryable failure must NOT clear the marker: attempts 2..N will re-walk and re-stream the
            // tree, so cascadeRunning has to stay true or the review screen would unlock Accept on a
            // half-resolved proposal during the retry gap. Clear only when this final attempt failed, so a
            // permanently-failed cascade does not leave Accept disabled forever.
            if (isFinalAttempt && payload.GateApply) {
                await ClearCascadeJobAsync(payload.EntityId, cascadeJobId, CancellationToken.None);
            }

            throw;
        }
    }

    /// <summary>
    /// Reports whether a background cascade still has a live destination: the queue item exists, is still
    /// being reviewed (a <see cref="IdentifyQueueState.Proposal"/>), and is still marked with this
    /// cascade's job id (or transiently unmarked). Returns false once the user removes the item or a
    /// newer search supersedes it, which is the signal for the cascade to stop walking and stop writing.
    /// </summary>
    private async Task<bool> IsCascadeActiveAsync(
        Guid entityId,
        Guid cascadeJobId,
        bool gateApply,
        string? expectedProposalId,
        CancellationToken cancellationToken) {
        var row = await _db.IdentifyQueueItems
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);
        if (row is not { State: IdentifyQueueState.Proposal }) {
            return false;
        }

        if (gateApply) {
            return row.CascadeJobId == cascadeJobId || row.CascadeJobId is null;
        }

        if (row.CascadeJobId is not null) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedProposalId)) {
            return true;
        }

        var storedProposal = Deserialize<EntityMetadataProposal>(row.ProposalJson);
        return string.Equals(storedProposal?.ProposalId, expectedProposalId, StringComparison.Ordinal);
    }

    private async Task SaveProposalSafelyAsync(Guid entityId, EntityMetadataProposal proposal, CancellationToken cancellationToken) {
        try {
            await SaveProposalAsync(entityId, proposal, cancellationToken);
        } catch (KeyNotFoundException) {
            // The item was deleted mid-cascade; nothing to persist.
        } catch (InvalidOperationException) {
            // The stored proposal was replaced (e.g. a new search); skip this stale write.
        }
    }

    /// <summary>
    /// Streams partial cascade roots onto the queue item via <see cref="SaveProposalAsync"/>, but only
    /// while the item is still queued and still owned by this cascade run. <see cref="IsActiveAsync"/>
    /// lets the walk stop early; the re-check inside <see cref="OnEntityResolvedAsync"/> closes the gap
    /// between that check and the write so a just-removed item is never revived by a late flush.
    /// </summary>
    private sealed class QueueProposalSink(
        IdentifyQueueService owner,
        Guid entityId,
        Guid cascadeJobId,
        bool gateApply,
        string? expectedProposalId) : IIdentifyCascadeSink {
        public Task<bool> IsActiveAsync(CancellationToken cancellationToken) =>
            owner.IsCascadeActiveAsync(entityId, cascadeJobId, gateApply, expectedProposalId, cancellationToken);

        public async Task OnEntityResolvedAsync(EntityMetadataProposal partialRoot, CancellationToken cancellationToken) {
            if (!await owner.IsCascadeActiveAsync(entityId, cascadeJobId, gateApply, expectedProposalId, cancellationToken)) {
                return;
            }

            await owner.SaveProposalSafelyAsync(entityId, partialRoot, cancellationToken);
        }
    }

    /// <summary>
    /// Applies the reviewed proposal and marks the queue item as done.
    /// </summary>
    public async Task<IdentifyQueueItem> ApplyAsync(
        Guid entityId,
        ApplyIdentifyQueueItemRequest request,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);

        (await _eligibility.EvaluateAsync(entityId, cancellationToken)).EnsureEligible();

        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Identify queue item for entity '{entityId}' was not found.");

        // Terminal rows are one-way. A Done/Deleted item still carries its ProposalJson (kept for
        // history), so without this guard a re-POST, double-click, or a bulk-accept loop hitting the same
        // entity twice would re-run the full recursive write. Reject instead of silently re-applying.
        if (row.State is IdentifyQueueState.Done or IdentifyQueueState.Deleted) {
            throw new InvalidOperationException(
                $"Identify queue item for entity '{entityId}' is already '{row.State.ToCode()}' and cannot be applied again.");
        }

        // A queued or running search means the stored ProposalJson is stale (the request cleared it,
        // or a new result is about to land): applying now would write the wrong metadata.
        if (row.State is IdentifyQueueState.Queued or IdentifyQueueState.Searching) {
            throw new InvalidOperationException(
                $"Identify queue item for entity '{entityId}' is awaiting its requested search; cannot apply yet.");
        }

        // Do not apply while the background cascade is still streaming the child tree: the stored proposal
        // is only partial until the cascade clears its marker, so applying now would drop the children
        // that have not streamed in yet. The single-item review disables Accept on this same signal;
        // enforce it here too so the bulk-accept path cannot apply a half-resolved tree.
        if (row.CascadeJobId is not null) {
            throw new InvalidOperationException(
                $"Identify cascade for entity '{entityId}' is still resolving children; cannot apply yet.");
        }

        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        var storedProposal = Deserialize<EntityMetadataProposal>(row.ProposalJson)
            ?? throw new InvalidOperationException("Identify queue item has no proposal to apply.");
        var proposal = request.Proposal ?? storedProposal;
        if (!string.Equals(proposal.ProposalId, storedProposal.ProposalId, StringComparison.Ordinal)) {
            throw new InvalidOperationException("Only the root identify proposal can be applied to a queue item.");
        }
        if (proposal.TargetKind.ToEntityKind() != entity.KindCode.DecodeAs<EntityKind>()) {
            throw new InvalidOperationException("Identify proposal kind does not match the queued entity.");
        }
        var preparedProposal = await _identify.PrepareApplyProposalAsync(
            entityId,
            proposal,
            cancellationToken);
        var acceptedProposal = AcceptedProposalMarker.MarkTreeOrganized(preparedProposal);
        IdentifyApplyProgressReporter? progressReporter = null;
        if (request.ProgressId is { } progressId) {
            _progress.Begin(progressId, entityId, CountApplySteps(acceptedProposal, request.SelectedFields));
            progressReporter = new IdentifyApplyProgressReporter(_progress, progressId);
        }

        try {
            var applied = await _identify.ApplyPreparedProposalAsync(
                entityId,
                acceptedProposal,
                request.SelectedFields,
                request.SelectedImages,
                progressReporter,
                cancellationToken);
            if (!applied) {
                throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
            }

            var now = DateTimeOffset.UtcNow;
            row.State = IdentifyQueueState.Done;
            row.ProposalJson = JsonSerializer.Serialize(acceptedProposal, JsonOptions);
            row.Error = null;
            row.UpdatedAt = now;
            row.CompletedAt = now;

            var entityRow = await _db.Entities.FindAsync([entityId], cancellationToken);
            if (entityRow is not null) {
                entityRow.IsOrganized = true;
                entityRow.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
            if (request.ProgressId is { } completedProgressId) {
                _progress.Complete(completedProgressId);
            }
        } catch (Exception ex) when (request.ProgressId is { } failedProgressId) {
            _progress.Fail(failedProgressId, ex.Message);
            throw;
        }

        var refreshedEntity = await LoadEntityAsync(entityId, cancellationToken) ?? entity;
        return MapRow(row, refreshedEntity);
    }

    /// <summary>
    /// Persists an updated in-progress proposal onto the queued entity (e.g. as children resolve)
    /// without applying it, so the accumulated proposal survives navigation and page refresh.
    /// </summary>
    public async Task<IdentifyQueueItem> SaveProposalAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(proposal);

        (await _eligibility.EvaluateAsync(entityId, cancellationToken)).EnsureEligible();

        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Identify queue item for entity '{entityId}' was not found.");
        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        var storedProposal = Deserialize<EntityMetadataProposal>(row.ProposalJson)
            ?? throw new InvalidOperationException("Identify queue item has no proposal to update.");
        if (!string.Equals(proposal.ProposalId, storedProposal.ProposalId, StringComparison.Ordinal)) {
            throw new InvalidOperationException("Only the queue item's own root proposal can be saved.");
        }

        row.ProposalJson = JsonSerializer.Serialize(proposal, JsonOptions);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return MapRow(row, entity);
    }

    /// <summary>
    /// Removes an item from the active identify queue without applying metadata.
    /// </summary>
    public async Task<IdentifyQueueItem?> DeleteAsync(Guid entityId, CancellationToken cancellationToken) {
        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);
        if (row is null) {
            return null;
        }

        var entity = await LoadEntityAsync(entityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{entityId}' was not found.");
        await RetireRowAsync(row, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return MapRow(row, entity);
    }

    private async Task<bool> RetireActiveTargetIfIneligibleAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var eligibility = await _eligibility.EvaluateAsync(entityId, cancellationToken);
        if (eligibility.IsEligible) {
            return false;
        }

        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);
        if (row is not null && row.State is not (IdentifyQueueState.Done or IdentifyQueueState.Deleted)) {
            await RetireRowAsync(row, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private async Task<bool> RetireIfIneligibleAsync(
        IdentifyQueueItemRow row,
        CancellationToken cancellationToken) {
        var eligibility = await _eligibility.EvaluateAsync(row.EntityId, cancellationToken);
        if (eligibility.IsEligible) {
            return false;
        }

        await RetireRowAsync(row, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task RetireRowAsync(IdentifyQueueItemRow row, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        await CancelCascadeAsync(row, cancellationToken);
        await CancelSearchJobAsync(row, cancellationToken);
        row.State = IdentifyQueueState.Deleted;
        row.Error = null;
        row.UpdatedAt = now;
        row.CompletedAt = now;
    }

    private async Task<IdentifyQueueItemRow> EnsureMutableRowAsync(Guid entityId, CancellationToken cancellationToken) {
        var row = await _db.IdentifyQueueItems
            .FirstOrDefaultAsync(item => item.EntityId == entityId, cancellationToken);
        if (row is not null) {
            return row;
        }

        await AddAsync(entityId, cancellationToken);
        return await _db.IdentifyQueueItems
            .FirstAsync(item => item.EntityId == entityId, cancellationToken);
    }

    private async Task<IReadOnlyList<IdentifyQueueItem>> MapRowsAsync(
        IReadOnlyList<IdentifyQueueItemRow> rows,
        CancellationToken cancellationToken) {
        var entityIds = rows.Select(row => row.EntityId).Distinct().ToArray();
        var entities = await _db.Entities
            .AsNoTracking()
            .Where(entity => entityIds.Contains(entity.Id))
            .ToDictionaryAsync(entity => entity.Id, cancellationToken);
        return rows
            .Where(row => entities.ContainsKey(row.EntityId))
            .Select(row => MapRow(row, entities[row.EntityId]))
            .ToArray();
    }

    private async Task<IdentifyQueueItem> MapRowAsync(IdentifyQueueItemRow row, CancellationToken cancellationToken) {
        var entity = await LoadEntityAsync(row.EntityId, cancellationToken)
            ?? throw new KeyNotFoundException($"Entity '{row.EntityId}' was not found.");
        return MapRow(row, entity);
    }

    private static IdentifyQueueItem MapRow(IdentifyQueueItemRow row, EntityRow entity) =>
        new(
            row.Id,
            row.EntityId,
            entity.KindCode.DecodeAs<EntityKind>(),
            entity.Title,
            entity.IsNsfw,
            row.State.ToCode(),
            row.ProviderCode,
            row.Action.ToCode(),
            Deserialize<IdentifyQuery>(row.QueryJson),
            Deserialize<IReadOnlyList<EntitySearchCandidate>>(row.CandidatesJson) ?? [],
            Deserialize<EntityMetadataProposal>(row.ProposalJson),
            row.Error,
            row.CascadeJobId is not null,
            row.CreatedAt,
            row.UpdatedAt,
            row.CompletedAt);

    private async Task<EntityRow?> LoadEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        await _db.Entities
            .FirstOrDefaultAsync(entity => entity.Id == entityId, cancellationToken);

    private static void ResetForSearch(IdentifyQueueItemRow row, DateTimeOffset now) {
        row.State = IdentifyQueueState.Search;
        row.ProviderCode = null;
        row.Action = IdentifyAction.Search;
        row.QueryJson = null;
        row.CandidatesJson = null;
        row.ProposalJson = null;
        row.Error = null;
        row.CascadeJobId = null;
        row.SearchJobId = null;
        row.UpdatedAt = now;
        row.CompletedAt = null;
    }

    private static IdentifyAction GuessAction(IdentifyQuery? query) {
        if (query?.ExternalIds is { Count: > 0 }) {
            return IdentifyAction.LookupId;
        }

        if (!string.IsNullOrWhiteSpace(query?.Url)) {
            return IdentifyAction.LookupUrl;
        }

        return IdentifyAction.Search;
    }

    private static IReadOnlyList<EntitySearchCandidate> ChoiceCandidates(EntityMetadataProposal proposal, EntityRow entity) {
        if (proposal.Candidates is { Count: > 0 }) {
            return proposal.Candidates;
        }

        if (proposal.Patch is null) {
            return [];
        }

        var title = !string.IsNullOrWhiteSpace(proposal.Patch.Title)
            ? proposal.Patch.Title.Trim()
            : entity.Title;
        var poster = ImageKindRoleResolver.Pick(proposal.Images, MediaImageKind.Poster, MediaImageKind.Still)?.Url;
        return [
            new EntitySearchCandidate(
                proposal.Patch.ExternalIds,
                title,
                YearFromDates(proposal.Patch.Dates),
                proposal.Patch.Description,
                poster,
                Popularity: null,
                CandidateId: proposal.ProposalId,
                Source: proposal.Provider,
                Confidence: proposal.Confidence,
                MatchReason: proposal.MatchReason)
        ];
    }

    private static int? YearFromDates(IReadOnlyDictionary<string, string> dates) {
        foreach (var key in new[] { "release", "firstAir", "airDate", "date" }) {
            if (dates.TryGetValue(key, out var value) &&
                DateOnly.TryParse(value, out var parsed)) {
                return parsed.Year;
            }
        }

        return null;
    }

    private static int CountApplySteps(EntityMetadataProposal proposal, IReadOnlyCollection<string> selectedFields) {
        var selected = selectedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var count = 1;
        if (selected.Contains("credits") || selected.Contains("studio") || selected.Contains("tags")) {
            count += CountRelationships(proposal);
        }

        count += proposal.Children
            .Where(child => !EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind))
            .Sum(CountStructuralApplySteps);
        return Math.Max(count, 1);
    }

    private static int CountStructuralApplySteps(EntityMetadataProposal proposal) {
        var count = 1;
        if (proposal.Patch.Credits.Count > 0 ||
            !string.IsNullOrWhiteSpace(proposal.Patch.Studio) ||
            proposal.Patch.Tags.Count > 0) {
            count += CountRelationships(proposal);
        }

        count += proposal.Children
            .Where(child => !EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind))
            .Sum(CountStructuralApplySteps);
        return count;
    }

    private static int CountRelationships(EntityMetadataProposal proposal) =>
        (proposal.Relationships ?? [])
            .Where(child => EntityMetadataProposalTraversal.IsRelationshipKind(child.TargetKind))
            .GroupBy(child => child.ProposalId, StringComparer.Ordinal)
            .Count();

    private static T? Deserialize<T>(string? json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
