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
            var listing = await GetListingAsync(client.Id, clients.Get(client.Kind), connection, listingCache, cancellationToken);
            if (listing is null) {
                // Couldn't read the client this pass — leave the acquisition untouched and try again later.
                return;
            }

            if (!listing.TryGetValue(transfer.ClientItemId.ToLowerInvariant(), out var status)) {
                // Genuinely absent from the client's full listing. Only declare removal after the grace
                // window so a brief client outage doesn't fail an otherwise-healthy download.
                if (DateTimeOffset.UtcNow - transfer.UpdatedAt >= RemovalGrace) {
                    await acquisitions.SetStatusAsync(transfer.AcquisitionId, AcquisitionStatus.Failed, "The download was removed from the client.", cancellationToken);
                } else {
                    logger.LogDebug("AcquisitionMonitor: transfer {TransferId} not in client listing; within grace window.", transfer.TransferId);
                }

                return;
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
