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

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var transfers = await acquisitions.ListActiveTransfersAsync(cancellationToken);
        if (transfers.Count == 0) {
            return;
        }

        var clientCache = new Dictionary<Guid, Contracts.Acquisition.DownloadClientDetail?>();
        var processed = 0;
        foreach (var transfer in transfers) {
            cancellationToken.ThrowIfCancellationRequested();
            await AdvanceTransferAsync(context, transfer, clientCache, cancellationToken);
            processed++;
            await context.ReportProgressAsync(processed * 100 / transfers.Count, "Polling transfers", cancellationToken);
        }
    }

    private async Task AdvanceTransferAsync(
        JobContext context,
        ActiveTransfer transfer,
        Dictionary<Guid, Contracts.Acquisition.DownloadClientDetail?> clientCache,
        CancellationToken cancellationToken) {
        var client = await ResolveClientAsync(transfer.DownloadClientConfigId, clientCache, cancellationToken);
        if (client is null) {
            return;
        }

        try {
            var connection = new DownloadClientConnection(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category);
            var status = await clients.Get(client.Kind).GetItemAsync(connection, transfer.ClientItemId, cancellationToken);
            if (status is null) {
                await acquisitions.SetStatusAsync(transfer.AcquisitionId, AcquisitionStatus.Failed, "The download was removed from the client.", cancellationToken);
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
