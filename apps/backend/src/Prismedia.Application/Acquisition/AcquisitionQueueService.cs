using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Sends a chosen release to the download client. Resolves the candidate's server-side download URL,
/// hands it to the default download client under the configured category, records the transfer, and
/// moves the acquisition to <see cref="AcquisitionStatus.Queued"/> for the monitor to track.
/// </summary>
public sealed class AcquisitionQueueService(
    IAcquisitionStore acquisitions,
    IAcquisitionBlocklistStore blocklist,
    IDownloadClientConfigStore downloadClients,
    IDownloadClientFactory clients,
    IReleaseLinkResolver linkResolver) : IAcquisitionQueueService {
    /// <summary>Queues a chosen candidate: resolves a usable link (direct, magnet, or scraped from the info page) and hands it to the download client.</summary>
    public async Task<AcquisitionDetail?> QueueAsync(Guid acquisitionId, Guid candidateId, CancellationToken cancellationToken) {
        var candidate = await acquisitions.GetQueueCandidateAsync(acquisitionId, candidateId, cancellationToken);
        if (candidate is null) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionReleaseNotFound, "The selected release was not found for this acquisition.");
        }

        if (candidate.Protocol != DownloadProtocol.Torrent) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionInvalid, "Only torrent releases can be downloaded in this version.");
        }

        // The blocklist is enforced at search time, but a stored candidate row can still carry a blocklisted
        // identity (e.g. the release that just failed). Refuse it here too so the manual "queue this release"
        // action can't bypass the blocklist.
        var identity = ReleaseIdentity.For(candidate.InfoHash, candidate.IndexerName, candidate.Title);
        if ((await blocklist.GetIdentitiesAsync(cancellationToken)).Contains(identity)) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "This release is blocklisted from a previous failed attempt. Remove it from the blocklist to download it again.");
        }

        var url = await ResolveUrlAsync(candidate, cancellationToken);
        if (string.IsNullOrWhiteSpace(url)) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionReleaseNotFound,
                "No download link could be found for this release. Open its page and upload the .torrent file manually.");
        }

        var (client, connection) = await ResolveClientAsync(cancellationToken);
        var downloadClient = clients.Get(client.Kind);
        // Re-queueing (after a failed/cancelled attempt) supersedes any prior download — drop the old
        // torrent from the client first so it doesn't linger as an orphan or collide on re-add.
        await RemovePriorTorrentAsync(acquisitionId, downloadClient, connection, cancellationToken);
        try {
            var clientItemId = await downloadClient
                .AddAsync(connection, new DownloadAddRequest(url, candidate.InfoHash, client.Category), cancellationToken);
            await acquisitions.CreateTransferAsync(acquisitionId, client.Id, clientItemId, client.Category, cancellationToken);
            // Snapshot the chosen release so a later failure can blocklist exactly it (and pick the next-best).
            await acquisitions.SetSelectedReleaseAsync(
                acquisitionId,
                new SelectedRelease(candidate.Title, candidate.IndexerName, candidate.InfoHash),
                cancellationToken);
            await acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Queued, "Sent to download client.", cancellationToken);
        } catch (AcquisitionConfigurationException) {
            throw;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            await acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, $"Failed to queue download: {ex.Message}", cancellationToken);
            throw new AcquisitionConfigurationException(ApiProblemCodes.DownloadClientUnreachable, $"Failed to queue download: {ex.Message}");
        }

        return await acquisitions.GetAsync(acquisitionId, cancellationToken);
    }

    /// <summary>Queues an acquisition from a user-supplied .torrent file (the manual fallback for linkless releases).</summary>
    public async Task<AcquisitionDetail?> QueueManualTorrentAsync(Guid acquisitionId, string fileName, byte[] torrent, CancellationToken cancellationToken) {
        if (await acquisitions.GetAsync(acquisitionId, cancellationToken) is null) {
            return null;
        }

        var (client, connection) = await ResolveClientAsync(cancellationToken);
        var downloadClient = clients.Get(client.Kind);
        await RemovePriorTorrentAsync(acquisitionId, downloadClient, connection, cancellationToken);
        try {
            var clientItemId = await downloadClient.AddTorrentFileAsync(connection, fileName, torrent, cancellationToken);
            await acquisitions.CreateTransferAsync(acquisitionId, client.Id, clientItemId, client.Category, cancellationToken);
            await acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Queued, "Uploaded torrent sent to download client.", cancellationToken);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            await acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, $"Failed to queue uploaded torrent: {ex.Message}", cancellationToken);
            throw new AcquisitionConfigurationException(ApiProblemCodes.DownloadClientUnreachable, $"Failed to queue uploaded torrent: {ex.Message}");
        }

        return await acquisitions.GetAsync(acquisitionId, cancellationToken);
    }

    /// <summary>Best-effort removal of an acquisition's prior torrent before a re-queue. Never blocks the new download.</summary>
    private async Task RemovePriorTorrentAsync(Guid acquisitionId, IDownloadClient downloadClient, DownloadClientConnection connection, CancellationToken cancellationToken) {
        var priorItemId = await acquisitions.GetTransferClientItemIdAsync(acquisitionId, cancellationToken);
        if (string.IsNullOrWhiteSpace(priorItemId)) {
            return;
        }

        try {
            await downloadClient.RemoveAsync(connection, priorItemId, deleteData: true, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception) {
            // The prior torrent may already be gone; the re-queue should still proceed.
        }
    }

    /// <summary>Direct link first, then magnet, then a magnet scraped from the release's info page.</summary>
    private async Task<string?> ResolveUrlAsync(AcquisitionQueueCandidate candidate, CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(candidate.DownloadUrl)) {
            return candidate.DownloadUrl;
        }

        if (!string.IsNullOrWhiteSpace(candidate.MagnetUrl)) {
            return candidate.MagnetUrl;
        }

        if (!string.IsNullOrWhiteSpace(candidate.InfoUrl)) {
            return await linkResolver.ResolveMagnetAsync(candidate.InfoUrl, cancellationToken);
        }

        return null;
    }

    private async Task<(Contracts.Acquisition.DownloadClientDetail Client, DownloadClientConnection Connection)> ResolveClientAsync(CancellationToken cancellationToken) {
        var client = await downloadClients.GetDefaultAsync(cancellationToken)
            ?? throw new AcquisitionConfigurationException(ApiProblemCodes.DownloadClientInvalid, "No download client is configured.");
        return (client, new DownloadClientConnection(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category));
    }
}
