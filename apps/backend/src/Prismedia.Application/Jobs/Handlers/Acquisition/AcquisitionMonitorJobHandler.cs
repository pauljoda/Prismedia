using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Polls the download client for every in-flight transfer and advances its acquisition: reports
/// progress while downloading and marks the acquisition <see cref="AcquisitionStatus.Downloaded"/>
/// once the client reports completion. Enqueued as a singleton by the scheduler whenever active
/// transfers exist, so a single pass covers all of them.
/// </summary>
public sealed class AcquisitionMonitorJobHandler(
    IAcquisitionStore acquisitions,
    IDownloadClientConfigStore downloadClients,
    IDownloadClientFactory clients,
    RemotePathMapper remotePaths,
    IAcquisitionHistoryStore history,
    ILogger<AcquisitionMonitorJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.AcquisitionMonitor;

    /// <summary>
    /// How long a torrent may stay absent from the download client's listing before the acquisition is
    /// treated as removed. Presence is checked against the client's full listing (see
    /// <see cref="IDownloadClient.ListItemsAsync"/>), so a momentary miss can't trigger this — but a brief
    /// client outage still shouldn't fail a healthy download, so removal is only declared after the torrent
    /// has been continuously absent for this window.
    /// </summary>
    private static readonly TimeSpan RemovalGrace = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a transfer may stay stalled (the client reports no peers/metadata, an error, or missing
    /// files — see <see cref="DownloadItemStatus.IsStalled"/>) before it is abandoned and handed to
    /// failed-download recovery. The grace window deliberately tolerates transient stalls — a torrent whose
    /// seeders briefly vanish should resume, not be blocklisted — so only a sustained stall is treated as a
    /// dead release.
    /// </summary>
    private static readonly TimeSpan StallGrace = TimeSpan.FromMinutes(60);

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var transfers = await acquisitions.ListActiveTransfersAsync(cancellationToken);
        var seeding = await acquisitions.ListSeedingTransfersAsync(cancellationToken);
        if (transfers.Count == 0 && seeding.Count == 0) {
            return;
        }

        var clientCache = new Dictionary<Guid, Contracts.Acquisition.DownloadClientDetail?>();
        // Authoritative per-client torrent listings, fetched at most once per pass. A null entry means the
        // listing could not be read this pass, in which case transfers are left untouched.
        var listingCache = new Dictionary<Guid, IReadOnlyDictionary<string, DownloadItemStatus>?>();
        var processed = 0;
        foreach (var transfer in transfers) {
            cancellationToken.ThrowIfCancellationRequested();
            await AdvanceTransferAsync(context, transfer, clientCache, listingCache, cancellationToken);
            processed++;
            await context.ReportProgressAsync(processed * 100 / Math.Max(transfers.Count, 1), "Polling transfers", cancellationToken);
        }

        foreach (var watch in seeding) {
            cancellationToken.ThrowIfCancellationRequested();
            await AdvanceSeedingAsync(watch, clientCache, cancellationToken);
        }
    }

    /// <summary>
    /// Advances one imported-and-still-seeding torrent: once EITHER seed goal is met (ratio, or time —
    /// from the client's own seeding clock when it reports one, else measured from the import), the
    /// torrent and its download-dir data are removed; the library file is a separate hardlink/copy and
    /// is untouched. A torrent the user already removed simply ends its watch.
    /// </summary>
    private async Task AdvanceSeedingAsync(
        SeedingTransfer watch,
        Dictionary<Guid, Contracts.Acquisition.DownloadClientDetail?> clientCache,
        CancellationToken cancellationToken) {
        var client = await ResolveClientAsync(watch.DownloadClientConfigId, clientCache, cancellationToken);
        if (client is null) {
            return;
        }

        try {
            var connection = new DownloadClientConnection(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category, client.ApiKey);
            var downloadClient = clients.Get(client.Kind);
            var properties = await downloadClient.GetPropertiesAsync(connection, watch.ClientItemId, cancellationToken);
            if (properties is null) {
                logger.LogDebug("AcquisitionMonitor: seeding torrent {ItemId} is gone from the client; ending its watch.", watch.ClientItemId);
                await acquisitions.ClearTransferSeedingAsync(watch.TransferId, cancellationToken);
                return;
            }

            var ratioMet = watch.GoalRatio is { } goalRatio && properties.Ratio is { } ratio && ratio >= goalRatio;
            var timeMet = watch.GoalTimeMinutes is { } goalMinutes
                && (properties.SeedingTimeSeconds is { } seconds
                    ? seconds >= goalMinutes * 60L
                    : DateTimeOffset.UtcNow - watch.SeedingSince >= TimeSpan.FromMinutes(goalMinutes));
            if (!ratioMet && !timeMet) {
                return;
            }

            logger.LogInformation(
                "AcquisitionMonitor: seed goal met for {ItemId} (ratio {Ratio}, seeded {Seconds}s); removing from the client.",
                watch.ClientItemId, properties.Ratio, properties.SeedingTimeSeconds);
            await downloadClient.RemoveAsync(connection, watch.ClientItemId, deleteData: true, cancellationToken);
            await acquisitions.ClearTransferSeedingAsync(watch.TransferId, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "AcquisitionMonitor: failed to advance seeding watch for transfer {TransferId}", watch.TransferId);
        }
    }

    private async Task AdvanceTransferAsync(
        JobContext context,
        ActiveTransfer transfer,
        Dictionary<Guid, Contracts.Acquisition.DownloadClientDetail?> clientCache,
        Dictionary<Guid, IReadOnlyDictionary<string, DownloadItemStatus>?> listingCache,
        CancellationToken cancellationToken) {
        var client = await ResolveClientAsync(transfer.DownloadClientConfigId, clientCache, cancellationToken);
        if (client is null) {
            return;
        }

        try {
            var connection = new DownloadClientConnection(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category, client.ApiKey);
            var downloadClient = clients.Get(client.Kind);
            var listing = await GetListingAsync(client.Id, downloadClient, connection, listingCache, cancellationToken);
            if (listing is null) {
                // Couldn't read the client this pass — leave the acquisition untouched and try again later.
                return;
            }

            if (!listing.TryGetValue(transfer.ClientItemId.ToLowerInvariant(), out var status)) {
                // Absent from the category-scoped listing. That can mean genuinely removed OR a healthy
                // torrent whose category drifted in the client (the category filter silently omits it), so
                // confirm with an unfiltered per-hash lookup before treating it as gone — otherwise a
                // recategorized but perfectly healthy download would be failed and permanently blocklisted.
                var direct = await downloadClient.GetItemAsync(connection, transfer.ClientItemId, cancellationToken);
                if (direct is null) {
                    // Genuinely gone — the user (or another tool) removed the download from the client. That
                    // is not evidence the release was bad, so nothing is blocklisted or failed; the
                    // acquisition falls back to a fresh release search instead, so it can never sit orphaned
                    // in Downloading against a torrent that no longer exists. Removal is only declared after
                    // the grace window so a brief client outage doesn't restart a healthy download.
                    if (DateTimeOffset.UtcNow - transfer.UpdatedAt >= RemovalGrace) {
                        await FallBackToSearchAsync(context, transfer.AcquisitionId, cancellationToken);
                    } else {
                        logger.LogDebug("AcquisitionMonitor: transfer {TransferId} not in client listing; within grace window.", transfer.TransferId);
                    }

                    return;
                }

                // Present after all (its category drifted) — advance from the direct status rather than failing.
                status = direct;
            }

            // The client reports paths in ITS filesystem view; the remote path mapping translates them
            // into Prismedia's before they are persisted, so the import reads real local paths.
            var localContentPath = await remotePaths.ToLocalAsync(client.Id, status.ContentPath, cancellationToken);
            await acquisitions.UpdateTransferAsync(transfer.TransferId, status.Progress, status.State, localContentPath, cancellationToken);

            if (status.IsFailed) {
                // The client says this download is definitively dead (e.g. a SABnzbd Failed history entry:
                // incomplete, unpack failed, or encrypted). Unlike a stall, it cannot recover, so it goes to
                // failed-download recovery immediately instead of waiting out the stall grace window.
                await EnqueueFailedHandleAsync(
                    context,
                    transfer.AcquisitionId,
                    BlocklistReason.Failed,
                    string.IsNullOrWhiteSpace(status.FailureMessage)
                        ? "The download client reported the download failed."
                        : $"The download client reported the download failed: {status.FailureMessage}",
                    cancellationToken);
                return;
            }

            if (status.IsComplete) {
                // An upgrade child does not import beside the owned book — it routes to the replace job, which
                // verifies the new file and atomically swaps it in. An ordinary acquisition imports normally.
                var isUpgrade = await acquisitions.GetUpgradeOwnedQualityAsync(transfer.AcquisitionId, cancellationToken) is not null;
                var completionJob = isUpgrade ? JobType.AcquisitionUpgradeReplace : JobType.AcquisitionImport;
                await acquisitions.SetStatusAsync(transfer.AcquisitionId, AcquisitionStatus.Downloaded, "Download complete; importing.", cancellationToken);
                await context.EnqueueIfNeededAsync(
                    new EnqueueJobRequest(
                        completionJob,
                        PayloadJson: AcquisitionJobPayload.Serialize(transfer.AcquisitionId),
                        TargetEntityId: transfer.AcquisitionId.ToString(),
                        TargetLabel: isUpgrade ? "Replace with upgrade" : "Import completed download"),
                    cancellationToken);
            } else {
                await AdvanceStallAsync(context, transfer, status, cancellationToken);
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "AcquisitionMonitor: failed to poll transfer {TransferId}", transfer.TransferId);
        }
    }

    /// <summary>
    /// Advances a transfer the client still reports as in-flight (not complete), handling stalls. A download
    /// is abandoned to failed-download recovery only when the client reports it stalled (no peers/metadata,
    /// errored, or missing files) AND it has made no progress since the previous pass, continuously for longer
    /// than <see cref="StallGrace"/>. Requiring both a stalled state and zero forward progress means a slow
    /// torrent that is still inching along — even one that momentarily reads stalled at a poll instant — is
    /// treated as alive and never blocklisted; only a genuinely dead release (stuck with no bytes moving) is
    /// abandoned. The anchor resets whenever the transfer makes progress, so the grace window measures a
    /// continuous, truly-stuck stretch. The terminal Failed transition is owned by the failed-handle job (as
    /// in the removal path); the monitor only surfaces the acquisition as actively downloading here.
    /// </summary>
    private async Task AdvanceStallAsync(JobContext context, ActiveTransfer transfer, DownloadItemStatus status, CancellationToken cancellationToken) {
        // A torrent that advanced since the last poll is alive even if it reads stalled this instant (slow,
        // not dead). Only a stalled torrent that has not moved is a stall candidate.
        var madeProgress = status.Progress > transfer.Progress;
        if (!status.IsStalled || madeProgress) {
            if (transfer.StalledSince is not null) {
                await acquisitions.MarkTransferStalledAsync(transfer.TransferId, null, cancellationToken);
            }

            if (transfer.AcquisitionStatus != AcquisitionStatus.Downloading) {
                await acquisitions.SetStatusAsync(transfer.AcquisitionId, AcquisitionStatus.Downloading, null, cancellationToken);
            }

            return;
        }

        // Stalled and not moving: anchor the stall on first observation and abandon only past the grace window.
        var stalledSince = transfer.StalledSince ?? DateTimeOffset.UtcNow;
        if (transfer.StalledSince is null) {
            await acquisitions.MarkTransferStalledAsync(transfer.TransferId, stalledSince, cancellationToken);
            logger.LogDebug("AcquisitionMonitor: transfer {TransferId} entered a stalled state ({State}).", transfer.TransferId, status.State);
        }

        if (DateTimeOffset.UtcNow - stalledSince >= StallGrace) {
            await EnqueueFailedHandleAsync(
                context,
                transfer.AcquisitionId,
                BlocklistReason.Stalled,
                "The download stalled with no usable peers and was abandoned.",
                cancellationToken);
            // Clear the anchor so abandonment is idempotent: if the monitor runs again before the recovery
            // job resolves this acquisition, the stall is re-measured from scratch rather than re-firing on
            // a stale anchor (which, once recovery has completed, could snapshot a different release).
            await acquisitions.MarkTransferStalledAsync(transfer.TransferId, null, cancellationToken);
            return;
        }

        // Still within the grace window: keep waiting, but make sure it reads as actively downloading (a fresh
        // transfer can stall straight out of Queued) rather than silently sitting.
        if (transfer.AcquisitionStatus != AcquisitionStatus.Downloading) {
            await acquisitions.SetStatusAsync(transfer.AcquisitionId, AcquisitionStatus.Downloading, null, cancellationToken);
        }
    }

    /// <summary>
    /// Falls an acquisition whose download vanished from the client back to the searching state: records a
    /// durable history event, moves the status out of Downloading (which retires the orphaned transfer from
    /// the active set and makes the acquisition searchable again), and enqueues the standard release search —
    /// which auto-grabs the best accepted release for wanted-entity acquisitions. The removed release is NOT
    /// blocklisted: a user delete says nothing about release quality, and the release picker's explicit
    /// blocklist action covers the "never grab that one again" case.
    /// </summary>
    private async Task FallBackToSearchAsync(JobContext context, Guid acquisitionId, CancellationToken cancellationToken) {
        const string message = "The download was removed from the client; searching for a release again.";
        var input = await acquisitions.GetSearchInputAsync(acquisitionId, cancellationToken);
        var selected = await acquisitions.GetSelectedReleaseAsync(acquisitionId, cancellationToken);
        await history.SafeAddAsync(logger, new AcquisitionHistoryEntry(
            acquisitionId,
            input?.EntityId,
            input?.Kind ?? EntityKind.Book,
            AcquisitionHistoryEvent.DownloadFailed,
            input?.Title ?? selected?.Title ?? "(removed acquisition)",
            selected?.Title,
            selected?.IndexerName,
            Message: message),
            cancellationToken);

        // The status change must precede the enqueue: the search handler skips non-searchable statuses
        // (Queued/Downloading among them), exactly to protect live grabs from stale monitor searches.
        await acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Searching, message, cancellationToken);
        await context.EnqueueIfNeededAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionSearch,
                PayloadJson: AcquisitionJobPayload.Serialize(acquisitionId),
                TargetEntityId: acquisitionId.ToString(),
                TargetLabel: input?.Title ?? "Search again after client removal"),
            cancellationToken);
        logger.LogInformation("AcquisitionMonitor: download for acquisition {AcquisitionId} is gone from the client; falling back to a new release search.", acquisitionId);
    }

    /// <summary>
    /// Hands a failed download (stalled past its grace window, or reported failed by the client) off to failed-download recovery.
    /// The release that was downloading is snapshotted into the payload here (before any manual re-queue can
    /// overwrite the acquisition's selected-release field) so the recovery job blocklists exactly the release
    /// that failed. Enqueue is deduped by target, so repeated passes while the job is pending enqueue it at
    /// most once.
    /// </summary>
    private async Task EnqueueFailedHandleAsync(JobContext context, Guid acquisitionId, BlocklistReason reason, string message, CancellationToken cancellationToken) {
        var selected = await acquisitions.GetSelectedReleaseAsync(acquisitionId, cancellationToken);
        await context.EnqueueIfNeededAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionFailedHandle,
                PayloadJson: AcquisitionFailedPayload.Serialize(acquisitionId, reason, message, selected),
                TargetEntityId: acquisitionId.ToString(),
                TargetLabel: "Recover failed download"),
            cancellationToken);
    }

    /// <summary>
    /// Returns the client's torrents keyed by lowercased hash, fetched at most once per pass. Returns null
    /// when the listing can't be read so callers leave their transfers untouched rather than treating every
    /// torrent as removed.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, DownloadItemStatus>?> GetListingAsync(
        Guid clientId,
        IDownloadClient client,
        DownloadClientConnection connection,
        Dictionary<Guid, IReadOnlyDictionary<string, DownloadItemStatus>?> cache,
        CancellationToken cancellationToken) {
        if (cache.TryGetValue(clientId, out var cached)) {
            return cached;
        }

        IReadOnlyDictionary<string, DownloadItemStatus>? map = null;
        try {
            var items = await client.ListItemsAsync(connection, cancellationToken);
            var byHash = new Dictionary<string, DownloadItemStatus>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items) {
                byHash[item.ClientItemId.ToLowerInvariant()] = item;
            }

            map = byHash;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "AcquisitionMonitor: failed to list items for download client {ClientId}", clientId);
        }

        cache[clientId] = map;
        return map;
    }

    private async Task<Contracts.Acquisition.DownloadClientDetail?> ResolveClientAsync(
        Guid? configId,
        Dictionary<Guid, Contracts.Acquisition.DownloadClientDetail?> cache,
        CancellationToken cancellationToken) {
        if (configId is not { } id) {
            return await downloadClients.GetDefaultAsync(cancellationToken);
        }

        if (cache.TryGetValue(id, out var cached)) {
            return cached;
        }

        var client = await downloadClients.GetAsync(id, cancellationToken) ?? await downloadClients.GetDefaultAsync(cancellationToken);
        cache[id] = client;
        return client;
    }
}
