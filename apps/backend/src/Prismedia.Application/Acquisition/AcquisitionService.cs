using Prismedia.Application.Jobs;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Application use case for the acquisition lifecycle from the API's perspective: create an acquisition
/// from request metadata and kick off a background release search, then list and read acquisition state.
/// </summary>
public sealed class AcquisitionService(
    IAcquisitionStore store,
    IJobQueueService queue,
    IDownloadClientConfigStore downloadClients,
    IDownloadClientFactory clients) {
    public Task<IReadOnlyList<AcquisitionSummary>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public Task<AcquisitionDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        store.GetAsync(id, cancellationToken);

    /// <summary>Cancels an acquisition: best-effort removes the torrent (and its data) from the client, then marks it cancelled.</summary>
    public async Task<AcquisitionDetail?> CancelAsync(Guid id, CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            return null;
        }

        var clientItemId = await store.GetTransferClientItemIdAsync(id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(clientItemId)) {
            var client = await downloadClients.GetDefaultAsync(cancellationToken);
            if (client is not null) {
                try {
                    var connection = new DownloadClientConnection(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category);
                    await clients.Get(client.Kind).RemoveAsync(connection, clientItemId, deleteData: true, cancellationToken);
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception) {
                    // The torrent may already be gone; cancellation should still succeed locally.
                }
            }
        }

        await store.SetStatusAsync(id, AcquisitionStatus.Cancelled, "Cancelled.", cancellationToken);
        return await store.GetAsync(id, cancellationToken);
    }

    /// <summary>Persists a new acquisition and enqueues the background search job that fills in candidates.</summary>
    public async Task<AcquisitionSummary> CreateAndSearchAsync(AcquisitionCreateRequest request, CancellationToken cancellationToken) {
        var metadata = new AcquisitionMetadata(
            request.Title.Trim(),
            string.IsNullOrWhiteSpace(request.Author) ? null : request.Author.Trim(),
            string.IsNullOrWhiteSpace(request.Series) ? null : request.Series.Trim(),
            request.Year,
            string.IsNullOrWhiteSpace(request.PosterUrl) ? null : request.PosterUrl.Trim(),
            string.IsNullOrWhiteSpace(request.PluginId) ? null : request.PluginId.Trim(),
            string.IsNullOrWhiteSpace(request.PluginItemId) ? null : request.PluginItemId.Trim(),
            request.RequestHistoryId);

        var summary = await store.CreateAsync(metadata, cancellationToken);
        await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionSearch,
                PayloadJson: AcquisitionJobPayload.Serialize(summary.Id),
                TargetEntityId: summary.Id.ToString(),
                TargetLabel: summary.Title),
            cancellationToken);
        return summary;
    }
}
