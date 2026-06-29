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

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var transfers = await acquisitions.ListActiveTransfersAsync(cancellationToken);
        if (transfers.Count == 0) {
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
            await context.ReportProgressAsync(processed * 100 / transfers.Count, "Polling transfers", cancellationToken);
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
            var connection = new DownloadClientConnection(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category);
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
                    // Genuinely gone. Only declare removal after the grace window so a brief client outage
                    // doesn't fail an otherwise-healthy download. The failed-handle job (not the monitor)
                    // owns the terminal Failed transition, so the acquisition stays active — deduped by
                    // target — until that job resolves it to Failed or a re-grab.
                    if (DateTimeOffset.UtcNow - transfer.UpdatedAt >= RemovalGrace) {
                        await EnqueueFailedHandleAsync(context, transfer.AcquisitionId, cancellationToken);
                    } else {
                        logger.LogDebug("AcquisitionMonitor: transfer {TransferId} not in client listing; within grace window.", transfer.TransferId);
                    }

                    return;
                }

                // Present after all (its category drifted) — advance from the direct status rather than failing.
                status = direct;
            }

            await acquisitions.UpdateTransferAsync(transfer.TransferId, status.Progress, status.State, status.ContentPath, cancellationToken);

            if (status.IsComplete) {
                await acquisitions.SetStatusAsync(transfer.AcquisitionId, AcquisitionStatus.Downloaded, "Download complete; importing.", cancellationToken);
                await context.EnqueueIfNeededAsync(
                    new EnqueueJobRequest(
                        JobType.AcquisitionImport,
                        PayloadJson: AcquisitionJobPayload.Serialize(transfer.AcquisitionId),
                        TargetEntityId: transfer.AcquisitionId.ToString(),
                        TargetLabel: "Import completed download"),
                    cancellationToken);
            } else if (transfer.AcquisitionStatus != AcquisitionStatus.Downloading) {
                await acquisitions.SetStatusAsync(transfer.AcquisitionId, AcquisitionStatus.Downloading, null, cancellationToken);
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "AcquisitionMonitor: failed to poll transfer {TransferId}", transfer.TransferId);
        }
    }

    /// <summary>
    /// Hands a vanished download off to failed-download recovery. The release that was downloading is
    /// snapshotted into the payload here (before any manual re-queue can overwrite the acquisition's
    /// selected-release field) so the recovery job blocklists exactly the release that failed. Enqueue is
    /// deduped by target, so repeated passes while the job is pending enqueue it at most once.
    /// </summary>
    private async Task EnqueueFailedHandleAsync(JobContext context, Guid acquisitionId, CancellationToken cancellationToken) {
        var selected = await acquisitions.GetSelectedReleaseAsync(acquisitionId, cancellationToken);
        const string message = "The download was removed from the client.";
        await context.EnqueueIfNeededAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionFailedHandle,
                PayloadJson: AcquisitionFailedPayload.Serialize(acquisitionId, BlocklistReason.Failed, message, selected),
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
