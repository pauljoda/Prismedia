using Prismedia.Application.Jobs;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
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
    IDownloadClientFactory clients,
    IImportedFilesReader importedFiles) {
    public Task<IReadOnlyList<AcquisitionSummary>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public Task<AcquisitionDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        store.GetAsync(id, cancellationToken);

    /// <summary>Live transfer telemetry (progress, speed, ETA, peers, per-piece state) for an in-flight acquisition, or null when there is no live transfer.</summary>
    public async Task<AcquisitionTransferView?> GetTransferAsync(Guid id, CancellationToken cancellationToken) {
        var info = await store.GetTransferInfoAsync(id, cancellationToken);
        if (info?.ClientItemId is not { } clientItemId) {
            return null;
        }

        var client = await ResolveClientAsync(info.DownloadClientConfigId, cancellationToken);
        if (client is null) {
            return null;
        }

        var connection = ConnectionFor(client);
        var download = clients.Get(client.Kind);
        // torrents/info (status) reports the live stage and progress even before metadata resolves;
        // torrents/properties adds speed/seeds/eta once the transfer is underway. Surface whatever is
        // available so early stages (e.g. fetching metadata) still show what's happening rather than a
        // bare "waiting" placeholder. Only when the torrent is gone entirely do we report no transfer.
        var status = await download.GetItemAsync(connection, clientItemId, cancellationToken);
        var properties = await download.GetPropertiesAsync(connection, clientItemId, cancellationToken);
        if (status is null && properties is null) {
            return null;
        }

        var pieces = await download.GetPieceStatesAsync(connection, clientItemId, cancellationToken);
        return new AcquisitionTransferView(
            status?.Progress ?? 0,
            status?.State,
            properties?.TotalSizeBytes ?? 0,
            properties?.DownloadSpeedBytesPerSecond ?? 0,
            properties?.EtaSeconds ?? 0,
            properties?.Seeds ?? 0,
            properties?.Peers ?? 0,
            properties?.SavePath ?? status?.SavePath,
            Array.ConvertAll(pieces, piece => (int)piece));
    }

    /// <summary>The acquisition's files: the imported library files once imported, otherwise the in-progress download files.</summary>
    public async Task<AcquisitionFilesView> GetFilesAsync(Guid id, CancellationToken cancellationToken) {
        var info = await store.GetTransferInfoAsync(id, cancellationToken);
        if (info is null) {
            return new AcquisitionFilesView(false, []);
        }

        if (!string.IsNullOrWhiteSpace(info.FinalSourcePath)) {
            var imported = importedFiles.List(info.FinalSourcePath);
            return new AcquisitionFilesView(true, imported.Select(ToFileItem).ToArray());
        }

        if (info.ClientItemId is { } clientItemId) {
            var client = await ResolveClientAsync(info.DownloadClientConfigId, cancellationToken);
            if (client is not null) {
                var files = await clients.Get(client.Kind).GetFilesAsync(ConnectionFor(client), clientItemId, cancellationToken);
                return new AcquisitionFilesView(false, files.Select(ToFileItem).ToArray());
            }
        }

        return new AcquisitionFilesView(false, []);
    }

    private async Task<Contracts.Acquisition.DownloadClientDetail?> ResolveClientAsync(Guid? configId, CancellationToken cancellationToken) =>
        configId is { } id
            ? await downloadClients.GetAsync(id, cancellationToken) ?? await downloadClients.GetDefaultAsync(cancellationToken)
            : await downloadClients.GetDefaultAsync(cancellationToken);

    private static DownloadClientConnection ConnectionFor(Contracts.Acquisition.DownloadClientDetail client) =>
        new(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category);

    private static AcquisitionFileItem ToFileItem(DownloadItemFile file) => new(file.Name, file.SizeBytes, file.Progress);

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
        if (string.IsNullOrWhiteSpace(request.Title)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionInvalid, "A title is required to start an acquisition.");
        }

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
