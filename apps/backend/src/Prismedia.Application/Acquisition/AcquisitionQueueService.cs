using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Sends a chosen release to a download client. Resolves the candidate's server-side download URL,
/// picks the highest-priority client for the release protocol, durably records cleanup ownership before
/// contacting it, hands the release over under the profile's download category (else the client's), fills
/// the client-native transfer id under a row lease, and moves the acquisition to
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
    IAcquisitionTransferAddCoordinator transferAdds,
    IAcquisitionHistoryStore history,
    ILogger<AcquisitionQueueService> logger,
    VideoScanConcurrencyGate? scanGate = null) : IAcquisitionQueueService {
    /// <summary>Queues a chosen candidate: resolves a usable link (direct, magnet, or scraped from the info page) and hands it to a download client.</summary>
    public async Task<AcquisitionDetail?> QueueAsync(
        Guid acquisitionId,
        Guid candidateId,
        CancellationToken cancellationToken,
        bool manualPick = false,
        AcquisitionStatus? requiredStatus = null) {
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
        if (ReleaseIdentity.IsListed(await blocklist.GetIdentitiesAsync(cancellationToken), candidate.InfoHash, candidate.IndexerName, candidate.Title)) {
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
        var indexerSeedGoal = await ResolveIndexerSeedGoalAsync(candidate, cancellationToken);
        var correlation = DownloadAddCorrelation.Create(candidate.InfoHash, candidate.Title);
        var existingAttempt = await acquisitions.GetTransferInfoAsync(acquisitionId, cancellationToken);
        DownloadClientDetail client;
        string attemptCategory;
        string? recoveredClientItemId = null;
        var createdPlaceholder = false;
        if (IsAdding(existingAttempt)) {
            if (!string.Equals(existingAttempt!.ClientItemId, correlation, StringComparison.Ordinal)) {
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "A different download-client handoff is awaiting recovery. Retry that exact release or finish its cleanup before queueing another.");
            }
            client = await ResolveAttemptOwnerAsync(existingAttempt, cancellationToken);
            attemptCategory = existingAttempt.Category ?? client.Category;
            recoveredClientItemId = await ResolveExistingAttemptItemAsync(
                client,
                attemptCategory,
                correlation,
                cancellationToken);
        } else {
            var priorStatus = await ClaimQueueLifecycleAsync(
                acquisitionId,
                requiredStatus,
                cancellationToken);
            // Re-queueing supersedes any prior download. Its exact pointer stays intact unless strict
            // removal succeeds; only then is it replaced by the durable pre-Add ownership placeholder.
            await RemovePriorDownloadOrRestoreQueueStateAsync(
                acquisitionId,
                priorStatus,
                eligible[0],
                cancellationToken);
            client = eligible[0];
            attemptCategory = category ?? client.Category;
            var seedGoal = ResolveSeedGoal(candidate, client, indexerSeedGoal);
            if (!await acquisitions.BeginTransferAddAsync(
                    acquisitionId,
                    client.Id,
                    correlation,
                    attemptCategory,
                    seedGoal,
                    CancellationToken.None)) {
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "The acquisition began cleanup before the download-client handoff. No release was queued; retry cleanup.");
            }
            createdPlaceholder = true;
        }

        var acquiredLease = await transferAdds.AcquireAsync(acquisitionId, cancellationToken);
        if (acquiredLease is null) {
            if (createdPlaceholder) {
                await acquisitions.AbandonTransferAddAsync(
                    acquisitionId,
                    client.Id,
                    correlation,
                    CancellationToken.None);
            }
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "The acquisition began cleanup before the download-client handoff. No release was queued; retry the cleanup operation.");
        }
        await using var addLease = acquiredLease;
        try {
            var connection = ConnectionFor(client) with { Category = attemptCategory };
            var downloadClient = clients.Get(client.Kind);
            var addedNow = recoveredClientItemId is null;
            var clientItemId = recoveredClientItemId
                ?? await downloadClient.AddAsync(
                    connection,
                    new DownloadAddRequest(url, candidate.InfoHash, attemptCategory, candidate.Title),
                    cancellationToken);
            var selectedRelease = new SelectedRelease(
                candidate.Title,
                candidate.IndexerName,
                candidate.InfoHash,
                manualPick);
            if (!await acquisitions.CompleteTransferAddAsync(
                    acquisitionId,
                    client.Id,
                    correlation,
                    clientItemId,
                    selectedRelease,
                    "Sent to download client.",
                    CancellationToken.None)) {
                await CompensateSupersededAddAsync(
                    acquisitionId,
                    client,
                    connection,
                    downloadClient,
                    correlation,
                    clientItemId,
                    addedNow);
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "The acquisition changed while the download-client handoff was finalizing. Any newly accepted client item was removed; refresh before retrying.");
            }
            await addLease.CommitAsync(CancellationToken.None);
            await RecordGrabbedAsync(acquisitionId, candidate.Title, candidate.IndexerName, client.DisplayName, cancellationToken);
        } catch (AcquisitionConfigurationException) {
            throw;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            // The pre-Add ownership placeholder remains Queued + adding. A recovered queue job retries the
            // same correlation; teardown claims the row lock, then resolves/removes a remotely accepted item.
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.DownloadClientUnreachable,
                $"The download-client handoff did not finish: {ex.Message}. Retry the same release; cleanup remains safely blocked until it is reconciled.");
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
        var correlation = TorrentInfoHash.TryComputeV1(torrent)
            ?? DownloadAddCorrelation.Create(infoHash: null, fileName);
        var existingAttempt = await acquisitions.GetTransferInfoAsync(acquisitionId, cancellationToken);
        DownloadClientDetail client;
        string attemptCategory;
        string? recoveredClientItemId = null;
        var createdPlaceholder = false;
        if (IsAdding(existingAttempt)) {
            if (!string.Equals(existingAttempt!.ClientItemId, correlation, StringComparison.Ordinal)) {
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "A different manual download handoff is awaiting recovery. Retry that exact file or finish its cleanup first.");
            }
            client = await ResolveAttemptOwnerAsync(existingAttempt, cancellationToken);
            attemptCategory = existingAttempt.Category ?? client.Category;
            recoveredClientItemId = await ResolveExistingAttemptItemAsync(
                client,
                attemptCategory,
                correlation,
                cancellationToken);
        } else {
            var priorStatus = await ClaimQueueLifecycleAsync(
                acquisitionId,
                requiredStatus: null,
                cancellationToken);
            await RemovePriorDownloadOrRestoreQueueStateAsync(
                acquisitionId,
                priorStatus,
                eligible[0],
                cancellationToken);
            client = eligible[0];
            attemptCategory = category ?? client.Category;
            var seedGoal = new TransferSeedGoal(client.SeedRatio, client.SeedTimeMinutes);
            if (!await acquisitions.BeginTransferAddAsync(
                    acquisitionId,
                    client.Id,
                    correlation,
                    attemptCategory,
                    seedGoal.IsEmpty ? null : seedGoal,
                    CancellationToken.None)) {
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "The acquisition began cleanup before the manual download handoff. No file was queued; retry cleanup.");
            }
            createdPlaceholder = true;
        }

        var acquiredLease = await transferAdds.AcquireAsync(acquisitionId, cancellationToken);
        if (acquiredLease is null) {
            if (createdPlaceholder) {
                await acquisitions.AbandonTransferAddAsync(
                    acquisitionId,
                    client.Id,
                    correlation,
                    CancellationToken.None);
            }
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "The acquisition began cleanup before the download-client handoff. No torrent was queued; retry the cleanup operation.");
        }
        await using var addLease = acquiredLease;
        try {
            var connection = ConnectionFor(client) with { Category = attemptCategory };
            var downloadClient = clients.Get(client.Kind);
            var addedNow = recoveredClientItemId is null;
            var clientItemId = recoveredClientItemId
                ?? await downloadClient.AddTorrentFileAsync(
                    connection,
                    fileName,
                    torrent,
                    cancellationToken);
            var selectedRelease = new SelectedRelease(
                fileName,
                IndexerName: null,
                InfoHash: null,
                ManualPick: true);
            if (!await acquisitions.CompleteTransferAddAsync(
                    acquisitionId,
                    client.Id,
                    correlation,
                    clientItemId,
                    selectedRelease,
                    "Uploaded torrent sent to download client.",
                    CancellationToken.None)) {
                await CompensateSupersededAddAsync(
                    acquisitionId,
                    client,
                    connection,
                    downloadClient,
                    correlation,
                    clientItemId,
                    addedNow);
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "The acquisition changed while the manual download handoff was finalizing. Any newly accepted client item was removed; refresh before retrying.");
            }
            await addLease.CommitAsync(CancellationToken.None);
            // A manual .torrent has no grab indexer; the uploaded file name stands in for the release title.
            await RecordGrabbedAsync(acquisitionId, fileName, indexerName: null, client.DisplayName, cancellationToken);
        } catch (AcquisitionConfigurationException) {
            throw;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.DownloadClientUnreachable,
                $"The manual download handoff did not finish: {ex.Message}. Retry the same file; cleanup remains safely blocked until it is reconciled.");
        }

        return await acquisitions.GetAsync(acquisitionId, cancellationToken);
    }

    /// <summary>
    /// A relational Add lease makes a mid-Add lifecycle win impossible: cancellation waits for the durable
    /// native pointer, then removes it. The in-memory adapter and defensive failure paths still compensate a
    /// newly accepted item when they observe that cancellation/teardown won before finalization. If the row
    /// remains Queued, the durable correlation is intentionally retained for crash recovery instead.
    /// </summary>
    private async Task CompensateSupersededAddAsync(
        Guid acquisitionId,
        DownloadClientDetail client,
        DownloadClientConnection connection,
        IDownloadClient downloadClient,
        string correlation,
        string clientItemId,
        bool addedNow) {
        if (!addedNow) {
            return;
        }

        var status = await acquisitions.GetStatusAsync(acquisitionId, CancellationToken.None);
        if (status == AcquisitionStatus.Queued) {
            return;
        }

        try {
            await downloadClient.RemoveAsync(
                connection,
                clientItemId,
                deleteData: true,
                CancellationToken.None);
            if (await downloadClient.GetItemAsync(
                    connection,
                    clientItemId,
                    CancellationToken.None) is not null) {
                throw new IOException(
                    "The download client still reports the newly accepted item after removal.");
            }
            await acquisitions.AbandonTransferAddAsync(
                acquisitionId,
                client.Id,
                correlation,
                CancellationToken.None);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(
                ex,
                "AcquisitionQueue: could not compensate remote item {ClientItemId} after acquisition {AcquisitionId} changed; retaining its durable correlation for teardown.",
                clientItemId,
                acquisitionId);
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.DownloadClientUnreachable,
                $"The acquisition changed after the download client accepted the item, and immediate removal failed: {ex.Message}. Its durable ownership marker was retained for cleanup.");
        }
    }

    private static readonly AcquisitionStatus[] QueueableStatuses = [
        AcquisitionStatus.AwaitingSelection,
        AcquisitionStatus.Failed,
        AcquisitionStatus.Cancelled,
        AcquisitionStatus.ManualImportRequired,
    ];

    private async Task<AcquisitionStatus> ClaimQueueLifecycleAsync(
        Guid acquisitionId,
        AcquisitionStatus? requiredStatus,
        CancellationToken cancellationToken) {
        var status = await acquisitions.GetStatusAsync(acquisitionId, cancellationToken);
        var isExpected = status is { } current
            && (requiredStatus is { } required
                ? current == required
                : QueueableStatuses.Contains(current));
        if (!isExpected) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                status is null
                    ? "The acquisition no longer exists."
                    : requiredStatus is not null
                        ? "The acquisition changed before its automatic release could be queued."
                        : $"A release cannot be changed while this acquisition is {status.Value.ToCode()}.");
        }

        AcquisitionImportContext? import;
        try {
            import = await acquisitions.GetImportContextAsync(acquisitionId, cancellationToken);
        } catch (InvalidDataException) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                ImportCheckpointLifecycle.CorruptCheckpointMessage);
        }
        if (import is { TvImportCheckpoint: not null } or { ImportPlacementCheckpoint: not null }
            && !await ImportCheckpointLifecycle.TryAbandonAsync(
                acquisitions,
                import,
                cancellationToken,
                scanGate)) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                ImportCheckpointLifecycle.CheckpointMustFinishMessage);
        }

        if (!await acquisitions.TryTransitionStatusAsync(
                acquisitionId,
                [status!.Value],
                AcquisitionStatus.Queued,
                "Preparing the selected release for the download client.",
                cancellationToken)) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "The acquisition changed while this release was being prepared. Refresh and try again.");
        }

        return status.Value;
    }

    /// <summary>
    /// Removes an old remote transfer only after the queue lifecycle is claimed. If strict removal fails,
    /// restores the exact prior queueable state with a non-cancellable compare-and-set; a concurrent
    /// teardown that already moved to Stopping wins and keeps its durable claim instead.
    /// </summary>
    private async Task RemovePriorDownloadOrRestoreQueueStateAsync(
        Guid acquisitionId,
        AcquisitionStatus priorStatus,
        DownloadClientDetail fallbackClient,
        CancellationToken cancellationToken) {
        try {
            await RemovePriorDownloadAsync(acquisitionId, fallbackClient, cancellationToken);
        } catch {
            await acquisitions.TryTransitionStatusAsync(
                acquisitionId,
                [AcquisitionStatus.Queued],
                priorStatus,
                "The prior download could not be removed; no replacement was queued.",
                CancellationToken.None);
            throw;
        }
    }

    private static bool IsAdding(AcquisitionTransferInfo? transfer) =>
        transfer?.State == TransferOwnershipState.Adding.ToCode()
        && !string.IsNullOrWhiteSpace(transfer.ClientItemId);

    private async Task<DownloadClientDetail> ResolveAttemptOwnerAsync(
        AcquisitionTransferInfo attempt,
        CancellationToken cancellationToken) {
        if (attempt.DownloadClientConfigId is not { } clientId
            || await downloadClients.GetAsync(clientId, cancellationToken) is not { } owner) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "The download client that owns the unfinished handoff is unavailable. Restore it before retrying or cleaning up.");
        }

        return owner;
    }

    /// <summary>
    /// Recovers a remotely accepted Add before retrying it. Direct hash/id lookup wins; otherwise exactly
    /// one normalized title match inside the recorded category is accepted. Ambiguity fails closed.
    /// </summary>
    private async Task<string?> ResolveExistingAttemptItemAsync(
        DownloadClientDetail client,
        string category,
        string correlation,
        CancellationToken cancellationToken) {
        var download = clients.Get(client.Kind);
        var connection = ConnectionFor(client) with { Category = category };
        if (await download.GetItemAsync(connection, correlation, cancellationToken) is { } direct) {
            return direct.ClientItemId;
        }

        var matches = (await download.ListItemsAsync(connection, cancellationToken))
            .Where(item => DownloadAddCorrelation.MatchesName(correlation, item.Name))
            .GroupBy(item => item.ClientItemId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (matches.Length > 1) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "The unfinished download handoff matches multiple client items in its category. Prismedia will not queue or remove another item until the duplicate is resolved.");
        }

        return matches.SingleOrDefault()?.ClientItemId;
    }

    /// <summary>
    /// Strict removal of an acquisition's prior download before a re-queue. The prior transfer may
    /// live in a different client than the new grab (e.g. a usenet retry after a torrent failure), so it
    /// is removed through its own recorded client, falling back to the new grab's first candidate for
    /// legacy transfers that recorded none. Re-queue must stop while this pointer is still live: replacing
    /// it with the new transfer would make the old client item unreachable to later teardown.
    /// </summary>
    private async Task RemovePriorDownloadAsync(Guid acquisitionId, DownloadClientDetail fallbackClient, CancellationToken cancellationToken) {
        var prior = await acquisitions.GetTransferInfoAsync(acquisitionId, cancellationToken);
        if (prior is null || string.IsNullOrWhiteSpace(prior.ClientItemId)) {
            return;
        }

        var owner = prior.DownloadClientConfigId is { } priorClientId
            ? await downloadClients.GetAsync(priorClientId, cancellationToken)
            : fallbackClient;
        if (owner is null) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "The download client that owns the prior transfer is unavailable. Restore it before re-queueing so the old download is not orphaned.");
        }

        try {
            var download = clients.Get(owner.Kind);
            var connection = ConnectionFor(owner);
            if (await download.GetItemAsync(connection, prior.ClientItemId, cancellationToken) is null) {
                return;
            }

            await download.RemoveAsync(connection, prior.ClientItemId, deleteData: true, cancellationToken);
            if (await download.GetItemAsync(connection, prior.ClientItemId, cancellationToken) is not null) {
                throw new IOException("The prior transfer is still present after the client acknowledged removal.");
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception exception) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.DownloadClientUnreachable,
                $"The prior download could not be removed, so the replacement was not queued: {exception.Message}");
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
    private async Task<TransferSeedGoal?> ResolveIndexerSeedGoalAsync(
        AcquisitionQueueCandidate candidate,
        CancellationToken cancellationToken) {
        if (candidate.Protocol != DownloadProtocol.Torrent) {
            return null;
        }

        var indexer = candidate.IndexerConfigId is { } indexerId
            ? await indexers.GetAsync(indexerId, cancellationToken)
            : null;
        return indexer is null
            ? null
            : new TransferSeedGoal(indexer.SeedRatio, indexer.SeedTimeMinutes);
    }

    private static TransferSeedGoal? ResolveSeedGoal(
        AcquisitionQueueCandidate candidate,
        DownloadClientDetail client,
        TransferSeedGoal? indexerGoal) {
        if (candidate.Protocol != DownloadProtocol.Torrent) {
            return null;
        }

        var goal = new TransferSeedGoal(
            indexerGoal?.Ratio ?? client.SeedRatio,
            indexerGoal?.TimeMinutes ?? client.SeedTimeMinutes);
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
