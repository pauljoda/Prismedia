using Microsoft.Extensions.Logging;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Sends a chosen release to a download client. Resolves the candidate's server-side download URL,
/// picks the client by the release's protocol in priority order (falling back to the next client of
/// that protocol when an add fails), hands it over under the profile's download category (else the
/// client's), records the transfer, and moves the acquisition to
/// <see cref="AcquisitionStatus.Queued"/> for the monitor to track.
/// </summary>
public sealed class AcquisitionQueueService(
    IAcquisitionStore acquisitions,
    IAcquisitionBlocklistStore blocklist,
    IDownloadClientConfigStore downloadClients,
    IDownloadClientFactory clients,
    IBookAcquisitionProfileStore profiles,
    IIndexerConfigStore indexers,
    IReleaseLinkResolver linkResolver,
    IAcquisitionHistoryStore history,
    ILogger<AcquisitionQueueService> logger) : IAcquisitionQueueService {
    /// <summary>Queues a chosen candidate: resolves a usable link (direct, magnet, or scraped from the info page) and hands it to a download client.</summary>
    public async Task<AcquisitionDetail?> QueueAsync(Guid acquisitionId, Guid candidateId, CancellationToken cancellationToken) {
        var candidate = await acquisitions.GetQueueCandidateAsync(acquisitionId, candidateId, cancellationToken);
        if (candidate is null) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionReleaseNotFound, "The selected release was not found for this acquisition.");
        }

        // The release protocol picks the clients: torrent releases go to torrent clients, usenet
        // releases to usenet clients. Resolving up front keeps the error actionable ("configure a
        // usenet client") instead of failing mid-add.
        var eligible = await ResolveClientsAsync(candidate.Protocol, cancellationToken);

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

        var category = await ResolveCategoryAsync(acquisitionId, cancellationToken);
        // Re-queueing (after a failed/cancelled attempt) supersedes any prior download — drop the old
        // one from its client first so it doesn't linger as an orphan or collide on re-add.
        await RemovePriorDownloadAsync(acquisitionId, eligible[0], cancellationToken);
        try {
            var (client, clientItemId) = await AddWithFallbackAsync(
                eligible,
                (downloadClient, connection) => downloadClient.AddAsync(connection, new DownloadAddRequest(url, candidate.InfoHash, category ?? connection.Category), cancellationToken));
            var seedGoal = await ResolveSeedGoalAsync(candidate, client, cancellationToken);
            await acquisitions.CreateTransferAsync(acquisitionId, client.Id, clientItemId, category ?? client.Category, cancellationToken, seedGoal);
            // Snapshot the chosen release so a later failure can blocklist exactly it (and pick the next-best).
            await acquisitions.SetSelectedReleaseAsync(
                acquisitionId,
                new SelectedRelease(candidate.Title, candidate.IndexerName, candidate.InfoHash),
                cancellationToken);
            await acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Queued, "Sent to download client.", cancellationToken);
            await RecordGrabbedAsync(acquisitionId, candidate.Title, candidate.IndexerName, client.DisplayName, cancellationToken);
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

        var eligible = await ResolveClientsAsync(DownloadProtocol.Torrent, cancellationToken);
        var category = await ResolveCategoryAsync(acquisitionId, cancellationToken);
        await RemovePriorDownloadAsync(acquisitionId, eligible[0], cancellationToken);
        try {
            var (client, clientItemId) = await AddWithFallbackAsync(
                eligible,
                (downloadClient, connection) => downloadClient.AddTorrentFileAsync(
                    connection with { Category = category ?? connection.Category }, fileName, torrent, cancellationToken));
            // A manual .torrent has no grab indexer; the client's default seed goal still applies.
            var seedGoal = new TransferSeedGoal(client.SeedRatio, client.SeedTimeMinutes);
            await acquisitions.CreateTransferAsync(acquisitionId, client.Id, clientItemId, category ?? client.Category, cancellationToken, seedGoal.IsEmpty ? null : seedGoal);
            await acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Queued, "Uploaded torrent sent to download client.", cancellationToken);
            // A manual .torrent has no grab indexer; the uploaded file name stands in for the release title.
            await RecordGrabbedAsync(acquisitionId, fileName, indexerName: null, client.DisplayName, cancellationToken);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            await acquisitions.SetStatusAsync(acquisitionId, AcquisitionStatus.Failed, $"Failed to queue uploaded torrent: {ex.Message}", cancellationToken);
            throw new AcquisitionConfigurationException(ApiProblemCodes.DownloadClientUnreachable, $"Failed to queue uploaded torrent: {ex.Message}");
        }

        return await acquisitions.GetAsync(acquisitionId, cancellationToken);
    }

    /// <summary>
    /// Tries each eligible client in priority order until one accepts the release. Only when every
    /// client fails does the queue attempt fail, carrying the last client's error.
    /// </summary>
    private async Task<(DownloadClientDetail Client, string ClientItemId)> AddWithFallbackAsync(
        IReadOnlyList<DownloadClientDetail> eligible,
        Func<IDownloadClient, DownloadClientConnection, Task<string>> add) {
        Exception? lastFailure = null;
        foreach (var client in eligible) {
            try {
                var clientItemId = await add(clients.Get(client.Kind), ConnectionFor(client));
                return (client, clientItemId);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                lastFailure = ex;
            }
        }

        throw lastFailure ?? new InvalidOperationException("No download client accepted the release.");
    }

    /// <summary>
    /// Best-effort removal of an acquisition's prior download before a re-queue. The prior transfer may
    /// live in a different client than the new grab (e.g. a usenet retry after a torrent failure), so it
    /// is removed through its own recorded client, falling back to the new grab's first candidate for
    /// legacy transfers that recorded none. Never blocks the new download.
    /// </summary>
    private async Task RemovePriorDownloadAsync(Guid acquisitionId, DownloadClientDetail fallbackClient, CancellationToken cancellationToken) {
        var prior = await acquisitions.GetTransferInfoAsync(acquisitionId, cancellationToken);
        if (prior is null || string.IsNullOrWhiteSpace(prior.ClientItemId)) {
            return;
        }

        try {
            var owner = prior.DownloadClientConfigId is { } priorClientId
                ? await downloadClients.GetAsync(priorClientId, cancellationToken) ?? fallbackClient
                : fallbackClient;
            await clients.Get(owner.Kind).RemoveAsync(ConnectionFor(owner), prior.ClientItemId, deleteData: true, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception) {
            // The prior download may already be gone; the re-queue should still proceed.
        }
    }

    /// <summary>
    /// Direct link first, then magnet, then a magnet scraped from the release's info page. The magnet
    /// fallbacks are torrent-only; a usenet release either carries its NZB link or cannot be queued.
    /// </summary>
    private async Task<string?> ResolveUrlAsync(AcquisitionQueueCandidate candidate, CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(candidate.DownloadUrl)) {
            return candidate.DownloadUrl;
        }

        if (candidate.Protocol != DownloadProtocol.Torrent) {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(candidate.MagnetUrl)) {
            return candidate.MagnetUrl;
        }

        if (!string.IsNullOrWhiteSpace(candidate.InfoUrl)) {
            return await linkResolver.ResolveMagnetAsync(candidate.InfoUrl, cancellationToken);
        }

        return null;
    }

    private async Task<IReadOnlyList<DownloadClientDetail>> ResolveClientsAsync(DownloadProtocol protocol, CancellationToken cancellationToken) {
        var eligible = await downloadClients.ListEnabledAsync(protocol, cancellationToken);
        if (eligible.Count == 0) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.DownloadClientInvalid,
                $"No enabled download client supports {protocol.ToCode()} releases. Configure one under Settings → Acquisition.");
        }

        return eligible;
    }

    /// <summary>
    /// The seed goal captured for the grab: the release indexer's ratio/time settings win, the
    /// client's defaults fill the gaps. Usenet transfers never seed, so they carry no goal.
    /// </summary>
    private async Task<TransferSeedGoal?> ResolveSeedGoalAsync(AcquisitionQueueCandidate candidate, DownloadClientDetail client, CancellationToken cancellationToken) {
        if (candidate.Protocol != DownloadProtocol.Torrent) {
            return null;
        }

        var indexer = candidate.IndexerConfigId is { } indexerId
            ? await indexers.GetAsync(indexerId, cancellationToken)
            : null;
        var goal = new TransferSeedGoal(
            indexer?.SeedRatio ?? client.SeedRatio,
            indexer?.SeedTimeMinutes ?? client.SeedTimeMinutes);
        return goal.IsEmpty ? null : goal;
    }

    /// <summary>The profile's download-category override for this acquisition's kind, or null for the client default.</summary>
    private async Task<string?> ResolveCategoryAsync(Guid acquisitionId, CancellationToken cancellationToken) {
        var input = await acquisitions.GetSearchInputAsync(acquisitionId, cancellationToken);
        return input is null ? null : await profiles.GetDownloadCategoryAsync(input.ProfileId, input.Kind, cancellationToken);
    }

    /// <summary>
    /// Records a durable Grabbed event for a just-queued release. Best-effort (a history hiccup must never
    /// fail the grab): the acquisition's title/kind/entity come from the search input it already reads for
    /// the category, and the quality code is detected from the release title in the kind's own vocabulary —
    /// a video/audio ladder code for media, the source tier for books (the format tier is only known once
    /// the file lands, so it is left to the Imported event).
    /// </summary>
    private async Task RecordGrabbedAsync(Guid acquisitionId, string releaseTitle, string? indexerName, string downloadClientName, CancellationToken cancellationToken) {
        var input = await acquisitions.GetSearchInputAsync(acquisitionId, cancellationToken);
        if (input is null) {
            return;
        }

        await history.SafeAddAsync(logger, new AcquisitionHistoryEntry(
            acquisitionId,
            input.EntityId,
            input.Kind,
            AcquisitionHistoryEvent.Grabbed,
            input.Title,
            releaseTitle,
            indexerName,
            downloadClientName,
            DetectGrabbedQualityCode(input.Kind, releaseTitle)),
            cancellationToken);
    }

    /// <summary>The release title's detected quality code for the log: a video/audio ladder code for media kinds, the source tier code for books.</summary>
    private static string DetectGrabbedQualityCode(EntityKind kind, string releaseTitle) =>
        MediaQualityLadder.IsVideoKind(kind) || MediaQualityLadder.IsAudioKind(kind)
            ? MediaQualityLadder.Detect(kind, releaseTitle).Code
            : BookFormatDetection.DetectSource(releaseTitle).ToCode();

    private static DownloadClientConnection ConnectionFor(DownloadClientDetail client) =>
        new(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category, client.ApiKey);
}
