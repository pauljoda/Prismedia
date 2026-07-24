using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Processes either one explicitly targeted Entity monitor or every monitor whose periodic action is due.
/// A still-missing acquisition monitor re-runs
/// the existing <see cref="JobType.AcquisitionSearch"/> — reusing the committed search → score →
/// (auto-pick) → download → import pipeline and its blocklist gate unchanged, so a wanted item is
/// fetched once an acceptable release appears. A container monitor runs a provider-backed child discovery
/// sync instead, surfacing missing works as wanted phantoms without downloading
/// anything on its own. The store reconciles fulfilled/orphaned monitors as part of listing the due set.
/// </summary>
public sealed class MonitoredSearchJobHandler(
    IMonitorStore monitors,
    IAcquisitionLifecycleStore acquisitions,
    SettingsService settings,
    RequestCommitService requests,
    ILogger<MonitoredSearchJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.MonitoredSearch;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var due = await ResolveWorkAsync(context.Job, cancellationToken);
        if (due.Count == 0) {
            return;
        }

        logger.LogInformation(
            "MonitoredSearch: processing {Count} {Scope} monitor(s).",
            due.Count,
            context.Job.TargetEntityKind == JobTargetKinds.Entity ? "targeted" : "due");
        var processed = 0;
        Exception? firstFailure = null;
        foreach (var monitor in due) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                var progressMessage = await ProcessMonitorAsync(
                    monitor,
                    context,
                    cancellationToken);
                if (progressMessage is not null) {
                    await context.ReportProgressAsync(
                        ++processed * 100 / due.Count,
                        progressMessage,
                        cancellationToken);
                }
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception exception) {
                firstFailure ??= exception;
                logger.LogError(
                    exception,
                    "MonitoredSearch: monitor {MonitorId} for '{Title}' failed; continuing the remaining sweep.",
                    monitor.MonitorId,
                    monitor.Title);
            }
        }

        if (firstFailure is not null) {
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }

    private async Task<string?> ProcessMonitorAsync(
        DueMonitor monitor,
        JobContext context,
        CancellationToken cancellationToken) {
        // The selected set is a snapshot. Revalidate the exact monitor before any request/search
        // action so a row claimed by destructive lifecycle work afterward is never acted on.
        if (!await monitors.IsActiveAsync(monitor.MonitorId, cancellationToken)) {
            return null;
        }

        // An imported structural unit that left gaps falls back to direct child acquisitions. The
        // Request registry and shared child traversal decide what those children are; this handler has
        // no season/episode, artist/album, or book/volume branch.
        if (monitor.MissingChildFallback && monitor.EntityId is { } parentEntityId) {
            var outcome = await requests.RequestMissingChildrenAsync(parentEntityId, cancellationToken);
            logger.LogInformation(
                "MonitoredSearch: '{Title}' imported with {Missing} missing child item(s); {Covered} covered by child acquisitions.",
                monitor.Title, outcome.Missing, outcome.Covered);

            // Keep the stable Entity monitor Active. Once child imports clear the wanted gaps, the next
            // sweep detaches the transient parent acquisition while preserving explicit monitor intent.
            await monitors.MarkSearchedAsync(monitor.MonitorId, cancellationToken);
            return $"Filling gaps in {monitor.Title}";
        }

        // Entity-only intent: containers sync provider children, source-backed leaves remain active,
        // and fileless leaves request themselves. Acquisition-linked dues always take the branch below.
        if (monitor.AcquisitionId is null && monitor.EntityId is { } watchedEntityId) {
            var maintained = await requests.MaintainAsync(
                watchedEntityId, monitor.BookRendition, cancellationToken);
            if (!maintained) {
                // False also means lifecycle contention (a child is being unmonitored or this Entity
                // is deleting files). That is transient and must never rewrite durable parent intent.
                // Keep the monitor Active and let the normal interval retry; permanent plugin loss is
                // visible through eligibility/settings rather than inferred from one failed sweep.
                logger.LogInformation(
                    "MonitoredSearch: Entity maintenance for monitor {MonitorId} did not run; keeping it Active for retry.",
                    monitor.MonitorId);
            }

            await monitors.MarkSearchedAsync(monitor.MonitorId, cancellationToken);
            return $"Syncing {monitor.Title}";
        }

        if (monitor.AcquisitionId is not { } acquisitionId) {
            return null; // defensive: a non-container due always carries its acquisition
        }

        // An upgrade-due monitor searches on a fresh CHILD acquisition (claimed atomically), so the
        // imported parent and its on-disk file are never touched by the search or its grab. A still-missing
        // monitor re-searches its own acquisition. Either way the search job is deduped per target.
        var searchTarget = acquisitionId;
        if (monitor.IsUpgrade) {
            Guid? childId = null;
            var accepted = monitor.EntityId is { } upgradeEntityId
                ? await monitors.ExecuteIfActiveEntityMutationAsync(
                    upgradeEntityId,
                    async leaseCancellationToken => childId = await monitors.CreateUpgradeChildAsync(
                        monitor.MonitorId,
                        leaseCancellationToken),
                    cancellationToken)
                : (childId = await monitors.CreateUpgradeChildAsync(
                    monitor.MonitorId,
                    cancellationToken)) is not null;
            if (!accepted) {
                return null;
            }
            if (childId is not { } id) {
                return null; // the upgrade slot was already taken — skip this monitor this pass
            }

            searchTarget = id;
        }

        // The active monitor is explicit durable provenance for a retry. Publish Searching BEFORE
        // enqueue so enqueue failure leaves recoverable intent, while redelivery after review/manual
        // state cannot infer a new search on its own.
        var searchStatus = await acquisitions.GetStatusAsync(searchTarget, cancellationToken);
        if (searchStatus is null || !AcquisitionSearchJobHandler.CanScheduleSearch(searchStatus.Value)) {
            return null;
        }
        if (searchStatus != AcquisitionStatus.Searching
            && !await acquisitions.TryTransitionStatusAsync(
                searchTarget,
                [searchStatus.Value],
                AcquisitionStatus.Searching,
                null,
                cancellationToken)) {
            return null;
        }

        await context.EnqueueIfNeededAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionSearch,
                PayloadJson: AcquisitionJobPayload.Serialize(searchTarget),
                TargetEntityId: searchTarget.ToString(),
                TargetLabel: monitor.Title),
            cancellationToken);
        await monitors.MarkSearchedAsync(monitor.MonitorId, cancellationToken);
        return $"Re-searching {monitor.Title}";
    }

    private async Task<IReadOnlyList<DueMonitor>> ResolveWorkAsync(
        JobRunSnapshot job,
        CancellationToken cancellationToken) {
        if (job.TargetEntityKind == JobTargetKinds.Entity
            && Guid.TryParse(job.TargetEntityId, out var entityId)) {
            var targeted = await monitors.ListImmediateForEntityAsync(entityId, cancellationToken);
            if (targeted.Count > 0) {
                return targeted;
            }

            // Compatibility for a resumed legacy monitor whose queued target predates stable Entity ids.
            return (await monitors.ListAsync(cancellationToken))
                .Where(monitor => monitor.Id == entityId)
                .Where(monitor => monitor.Status == MonitorStatus.Active)
                .Select(monitor => new DueMonitor(
                    monitor.Id,
                    monitor.AcquisitionId,
                    monitor.Title,
                    EntityId: monitor.EntityId,
                    BookRendition: monitor.BookRendition))
                .ToArray();
        }

        var config = await settings.GetMonitoredSearchSettingsAsync(cancellationToken);
        return await monitors.ListDueMonitorsAsync(config.IntervalMinutes, cancellationToken);
    }
}
