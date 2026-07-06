using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// The narrow acquisition seam a request commit needs: start an acquisition for a wanted entity and
/// check whether one already targets it. Implemented by <see cref="AcquisitionService"/>; kept separate
/// (mirroring <see cref="IAcquisitionQueueService"/>) so the commit flow doesn't couple to the full service.
/// </summary>
public interface IAcquisitionRequestService {
    /// <summary>Persists a new acquisition and enqueues the background search job that fills in candidates.</summary>
    Task<AcquisitionSummary> CreateAndSearchAsync(AcquisitionCreateRequest request, CancellationToken cancellationToken);

    /// <summary>True when any acquisition targets this wanted library entity.</summary>
    Task<bool> AnyForEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>Every acquisition targeting this wanted library entity, for teardown when the want is removed.</summary>
    Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>Removes an acquisition entirely: best-effort deletes its torrent (and data) from the client, then hard-deletes the record.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Application use case for the acquisition lifecycle from the API's perspective: create an acquisition
/// from request metadata and kick off a background release search, then list and read acquisition state.
/// </summary>
public sealed class AcquisitionService(
    IAcquisitionStore store,
    IAcquisitionBlocklistStore blocklist,
    IJobQueueService queue,
    IDownloadClientConfigStore downloadClients,
    IDownloadClientFactory clients,
    IImportedFilesReader importedFiles,
    IAcquisitionHistoryStore history,
    ILogger<AcquisitionService> logger,
    Requests.IWantedEntityWriter wantedEntities) : IAcquisitionRequestService {
    public Task<IReadOnlyList<AcquisitionSummary>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public Task<AcquisitionDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        store.GetAsync(id, cancellationToken);

    /// <summary>The latest acquisition backing a library entity (wanted or imported), or null when it has none.</summary>
    public Task<AcquisitionDetail?> GetForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        store.GetLatestForEntityAsync(entityId, cancellationToken);

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

    /// <summary>
    /// The global Downloads view: every active acquisition (not imported, not cancelled) with live
    /// download-client telemetry where a transfer is in flight. Telemetry is collected with one item
    /// listing per download client plus one properties read per transfer; an unreachable client degrades
    /// its rows to the last persisted progress instead of failing the view.
    /// </summary>
    public async Task<IReadOnlyList<DownloadQueueItemView>> ListDownloadsAsync(CancellationToken cancellationToken) {
        var summaries = await store.ListAsync(cancellationToken);
        var active = summaries
            .Where(summary => summary.Status is not (AcquisitionStatus.Imported or AcquisitionStatus.Cancelled))
            .ToArray();
        if (active.Length == 0) {
            return [];
        }

        var telemetry = await CollectLiveTelemetryAsync(cancellationToken);
        return active
            .Select(summary => {
                var live = telemetry.GetValueOrDefault(summary.Id);
                return new DownloadQueueItemView(
                    summary.Id,
                    summary.Kind,
                    summary.Title,
                    summary.Status,
                    summary.StatusMessage,
                    live?.Progress ?? summary.Progress,
                    summary.UpdatedAt,
                    summary.EntityId,
                    summary.PosterUrl,
                    live?.State,
                    live?.TotalSizeBytes,
                    live?.DownloadSpeedBytesPerSecond,
                    live?.EtaSeconds,
                    live?.Seeds,
                    live?.Peers,
                    live?.ClientName,
                    summary.Author,
                    summary.Series,
                    summary.Year);
            })
            .ToArray();
    }

    /// <summary>Live telemetry per acquisition id, grouped so each download client is listed once.</summary>
    private async Task<IReadOnlyDictionary<Guid, LiveTransferTelemetry>> CollectLiveTelemetryAsync(CancellationToken cancellationToken) {
        var transfers = await store.ListActiveTransfersAsync(cancellationToken);
        var telemetry = new Dictionary<Guid, LiveTransferTelemetry>();
        foreach (var clientTransfers in transfers.GroupBy(transfer => transfer.DownloadClientConfigId)) {
            var client = await ResolveClientAsync(clientTransfers.Key, cancellationToken);
            if (client is null) {
                continue;
            }

            var connection = ConnectionFor(client);
            var download = clients.Get(client.Kind);
            IReadOnlyDictionary<string, DownloadItemStatus> items;
            try {
                items = (await download.ListItemsAsync(connection, cancellationToken))
                    .ToDictionary(item => item.ClientItemId, StringComparer.OrdinalIgnoreCase);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                // The client being down must not take the whole Downloads view with it.
                logger.LogWarning(ex, "Download client {Client} unreachable while building the downloads view.", client.DisplayName);
                continue;
            }

            foreach (var transfer in clientTransfers) {
                if (!items.TryGetValue(transfer.ClientItemId, out var status)) {
                    continue;
                }

                DownloadItemProperties? properties = null;
                try {
                    properties = await download.GetPropertiesAsync(connection, transfer.ClientItemId, cancellationToken);
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception) {
                    // Properties are enrichment; the listing's progress/state still renders the row.
                }

                telemetry[transfer.AcquisitionId] = new LiveTransferTelemetry(
                    status.Progress,
                    status.State,
                    properties?.TotalSizeBytes,
                    properties?.DownloadSpeedBytesPerSecond,
                    properties?.EtaSeconds,
                    properties?.Seeds,
                    properties?.Peers,
                    client.DisplayName);
            }
        }

        return telemetry;
    }

    /// <summary>Live client telemetry for one acquisition's transfer, shaped for the Downloads view.</summary>
    private sealed record LiveTransferTelemetry(
        double Progress,
        string? State,
        long? TotalSizeBytes,
        double? DownloadSpeedBytesPerSecond,
        long? EtaSeconds,
        int? Seeds,
        int? Peers,
        string ClientName);

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

    private async Task<Contracts.Acquisition.DownloadClientDetail?> ResolveRemovalClientAsync(
        AcquisitionTransferInfo transfer,
        CancellationToken cancellationToken) =>
        transfer.DownloadClientConfigId is { } id
            ? await downloadClients.GetAsync(id, cancellationToken)
            : await downloadClients.GetDefaultAsync(cancellationToken);

    private static DownloadClientConnection ConnectionFor(Contracts.Acquisition.DownloadClientDetail client) =>
        new(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category, client.ApiKey);

    private static AcquisitionFileItem ToFileItem(DownloadItemFile file) => new(file.Name, file.SizeBytes, file.Progress);

    /// <summary>Cancels an acquisition: best-effort removes the download (and its data) from the owning client, then marks it cancelled.</summary>
    public async Task<AcquisitionDetail?> CancelAsync(Guid id, CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            return null;
        }

        var transfer = await store.GetTransferInfoAsync(id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(transfer?.ClientItemId)) {
            var client = await ResolveRemovalClientAsync(transfer, cancellationToken);
            if (client is not null) {
                try {
                    await clients.Get(client.Kind).RemoveAsync(ConnectionFor(client), transfer.ClientItemId, deleteData: true, cancellationToken);
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception) {
                    // The download may already be gone; cancellation should still succeed locally.
                }
            }
        }

        await store.SetStatusAsync(id, AcquisitionStatus.Cancelled, "Cancelled.", cancellationToken);
        await RecordRemovedAsync(detail.Summary, "Cancelled by user.", cancellationToken);

        // Cancelling a request removes the wanted placeholder it created (locked decision: cancel deletes).
        // A no-op when the entity already imported a file or the user deleted it from the library first.
        if (detail.Summary.EntityId is { } wantedEntityId) {
            await wantedEntities.DeleteIfWantedAsync(wantedEntityId, cancellationToken);
        }

        return await store.GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Removes an acquisition entirely: best-effort deletes its download (and data) from the owning client, then
    /// hard-deletes the record — and, like cancel, removes the wanted placeholder entity it created.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
        var summary = (await store.GetAsync(id, cancellationToken))?.Summary;
        var wantedEntityId = summary?.EntityId;
        // Record the Removed event BEFORE the hard delete so the entry still carries a live acquisition id;
        // the acquisition_history FK is SetNull, so once the row is deleted the entry's acquisition id nulls
        // out but the entry (and its denormalized title/kind/entity) survives — the durable audit trail.
        if (summary is not null) {
            await RecordRemovedAsync(summary, "Removed by user.", cancellationToken);
        }

        var transfer = await store.GetTransferInfoAsync(id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(transfer?.ClientItemId)) {
            var client = await ResolveRemovalClientAsync(transfer, cancellationToken);
            if (client is not null) {
                try {
                    await clients.Get(client.Kind).RemoveAsync(ConnectionFor(client), transfer.ClientItemId, deleteData: true, cancellationToken);
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception) {
                    // The download may already be gone; removal should still delete the record.
                }
            }
        }

        var deleted = await store.DeleteAsync(id, cancellationToken);
        if (deleted && wantedEntityId is { } entityId) {
            await wantedEntities.DeleteIfWantedAsync(entityId, cancellationToken);
        }

        return deleted;
    }

    /// <summary>
    /// Manually blocklists one of an acquisition's release candidates so it is never grabbed (here or on a
    /// future search) and marks that candidate rejected so the picker reflects it immediately. Returns the
    /// refreshed acquisition, or null when the candidate no longer exists.
    /// </summary>
    public async Task<AcquisitionDetail?> BlocklistCandidateAsync(Guid id, Guid candidateId, CancellationToken cancellationToken) {
        var candidate = await store.GetQueueCandidateAsync(id, candidateId, cancellationToken);
        if (candidate is null) {
            return null;
        }

        var identity = ReleaseIdentity.For(candidate.InfoHash, candidate.IndexerName, candidate.Title);
        await blocklist.AddAsync(
            new BlocklistAddRequest(identity, BlocklistReason.Manual, candidate.Title, candidate.IndexerName, candidate.InfoHash, id, "Blocklisted from the release picker."),
            cancellationToken);
        await store.MarkCandidatesBlocklistedAsync(id, identity, cancellationToken);
        return await store.GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Re-runs the release search for an existing acquisition on demand (the manual counterpart to monitoring).
    /// Enqueues the standard <see cref="JobType.AcquisitionSearch"/> — deduped per acquisition, and the handler
    /// re-checks that the acquisition is still searchable — so it can't disturb an in-flight grab. Returns the
    /// acquisition, or null when it no longer exists.
    /// </summary>
    public async Task<AcquisitionDetail?> ReSearchAsync(Guid id, CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            return null;
        }

        await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionSearch,
                PayloadJson: AcquisitionJobPayload.Serialize(id),
                TargetEntityId: id.ToString(),
                TargetLabel: detail.Summary.Title),
            cancellationToken);
        return detail;
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
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.Kind,
            request.EntityId,
            request.ProfileId,
            request.TargetLibraryRootId,
            request.SeasonNumber,
            request.EpisodeNumber);

        var summary = await store.CreateAsync(metadata, cancellationToken);
        await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionSearch,
                PayloadJson: AcquisitionJobPayload.Serialize(summary.Id),
                TargetEntityId: summary.Id.ToString(),
                TargetLabel: summary.Title),
            cancellationToken);

        // When the request came from a metadata plugin, enrich the held metadata in the background from the
        // provider (cover, fuller description, dates the lightweight search result lacked), so the acquisition
        // surface fills in and the imported book can be seeded. Best-effort — never blocks the request.
        if (!string.IsNullOrWhiteSpace(metadata.PluginId) && !string.IsNullOrWhiteSpace(metadata.PluginItemId)) {
            await queue.EnqueueAsync(
                new EnqueueJobRequest(
                    JobType.AcquisitionEnrich,
                    PayloadJson: AcquisitionJobPayload.Serialize(summary.Id),
                    TargetEntityId: summary.Id.ToString(),
                    TargetLabel: summary.Title),
                cancellationToken);
        }

        return summary;
    }

    /// <inheritdoc />
    public Task<bool> AnyForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        store.AnyForEntityAsync(entityId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        store.ListIdsForEntityAsync(entityId, cancellationToken);

    /// <summary>
    /// The durable acquisition activity log, newest-first. <paramref name="limit"/> is clamped by the store
    /// (default 200, max 500); <paramref name="entityId"/> optionally restricts it to one entity's events.
    /// </summary>
    public Task<IReadOnlyList<AcquisitionHistoryView>> ListHistoryAsync(int limit, Guid? entityId, CancellationToken cancellationToken) =>
        history.ListAsync(limit, entityId, cancellationToken);

    /// <summary>Records a durable Removed event for an acquisition being cancelled or deleted. Best-effort.</summary>
    private Task RecordRemovedAsync(AcquisitionSummary summary, string message, CancellationToken cancellationToken) =>
        history.SafeAddAsync(logger, new AcquisitionHistoryEntry(
            summary.Id,
            summary.EntityId,
            summary.Kind,
            AcquisitionHistoryEvent.Removed,
            summary.Title,
            Message: message),
            cancellationToken);
}
