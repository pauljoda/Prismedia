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
    IDownloadClientConfigStore downloadClients,
    IDownloadClientFactory clients) {
    public async Task<AcquisitionDetail?> QueueAsync(Guid acquisitionId, Guid candidateId, CancellationToken cancellationToken) {
        var candidate = await acquisitions.GetQueueCandidateAsync(acquisitionId, candidateId, cancellationToken);
        if (candidate is null) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionReleaseNotFound, "The selected release was not found for this acquisition.");
        }

        if (candidate.Protocol != DownloadProtocol.Torrent) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionInvalid, "Only torrent releases can be downloaded in this version.");
        }

        var url = !string.IsNullOrWhiteSpace(candidate.DownloadUrl) ? candidate.DownloadUrl : candidate.MagnetUrl;
        if (string.IsNullOrWhiteSpace(url)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionReleaseNotFound, "The selected release has no download link.");
        }

        var client = await downloadClients.GetDefaultAsync(cancellationToken)
            ?? throw new AcquisitionConfigurationException(ApiProblemCodes.DownloadClientInvalid, "No download client is configured.");

        var connection = new DownloadClientConnection(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category);
        try {
            var clientItemId = await clients.Get(client.Kind)
                .AddAsync(connection, new DownloadAddRequest(url, candidate.InfoHash, client.Category), cancellationToken);
            await acquisitions.CreateTransferAsync(acquisitionId, client.Id, clientItemId, client.Category, cancellationToken);
            await acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Queued, "Sent to download client.", cancellationToken);
        } catch (AcquisitionConfigurationException) {
            throw;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            await acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, $"Failed to queue download: {ex.Message}", cancellationToken);
            throw new AcquisitionConfigurationException(ApiProblemCodes.DownloadClientUnreachable, $"Failed to queue download: {ex.Message}");
        }

        return await acquisitions.GetAsync(acquisitionId, cancellationToken);
    }
}
