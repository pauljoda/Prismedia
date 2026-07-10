using Microsoft.Extensions.Logging;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// The narrow acquisition seam entity/request workflows need: start and inspect acquisitions for wanted
/// entities, tear them down when the want is removed, or replace an imported row with a clean reacquisition
/// after its files are deleted. Implemented by <see cref="AcquisitionService"/> so those workflows do not
/// couple to the full acquisition API service.
/// </summary>
public interface IAcquisitionRequestService {
    /// <summary>Persists a new acquisition and enqueues the background search job that fills in candidates.</summary>
    Task<AcquisitionSummary> CreateAndSearchAsync(AcquisitionCreateRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Persists and schedules an acquisition while the caller already holds the target Entity lifecycle
    /// lease. Request commit uses this seam inside its wider suppression/monitor intent transaction so it
    /// does not recursively reacquire the same locks. Other callers must use <see cref="CreateAndSearchAsync"/>.
    /// The default keeps narrow test adapters source-compatible.
    /// </summary>
    Task<AcquisitionSummary> CreateAndSearchWithinEntityLifecycleAsync(
        AcquisitionCreateRequest request,
        CancellationToken cancellationToken) =>
        CreateAndSearchAsync(request, cancellationToken);

    /// <summary>
    /// True when actionable acquisition work already targets the Entity. Imported and Cancelled rows are
    /// terminal history, not open requests: a fileless monitored Entity must be able to start fresh work.
    /// </summary>
    Task<bool> AnyOpenForEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>
    /// Every acquisition owned by this library Entity, including upgrade descendants whose stable
    /// ownership is inherited through <c>UpgradeOfAcquisitionId</c>. Destructive Entity lifecycles must
    /// close this complete graph so an in-flight replacement transfer cannot leak.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>
    /// Read-only preflight for destructive removal. An active or partially applied import rejects the
    /// operation; every other lifecycle state can be cancelled and removed. Missing rows are already
    /// removed and therefore eligible.
    /// </summary>
    Task<AcquisitionRemovalEligibility> GetRemovalEligibilityAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads whether an imported acquisition can be replaced after its owned files are deleted. This check
    /// has no persistence, queue, monitor, download-client, or filesystem side effects, so destructive entity
    /// workflows can validate their complete replacement set before mutating anything.
    /// </summary>
    Task<AcquisitionReacquireEligibility> GetReacquireEligibilityAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Durably claims an acquisition for the given destructive completion intent and cancels every exact
    /// queued/running acquisition job. An existing matching claim is an idempotent success; a competing
    /// intent or lifecycle change is an actionable conflict. False means the acquisition is already gone.
    /// </summary>
    Task<bool> ClaimTeardownAsync(
        Guid id,
        AcquisitionTeardownIntent intent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Strictly removes the recorded remote transfer and its data, or confirms it is already absent,
    /// without changing local acquisition, monitor, job, or history state. Missing/unreachable client
    /// configuration and client failures are actionable conflicts. Destructive Entity workflows call this
    /// for their complete operation set before touching local files or database state.
    /// </summary>
    Task ConfirmTransferRemovedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Hard-deletes a durably claimed acquisition after external/file work completed. The persisted intent
    /// must match, preventing a retry or competing workflow from changing reacquisition into removal.
    /// </summary>
    Task<bool> CompleteTeardownAsync(
        Guid id,
        AcquisitionTeardownIntent intent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes an acquisition entirely: best-effort deletes its torrent (and data) from the client, then
    /// hard-deletes the record. <paramref name="preserveWantedLoop"/> (the user-facing Downloads remove)
    /// keeps a monitor watching the acquisition alive by re-pointing it at a fresh pending clone; internal
    /// teardown paths leave it off — they are dismantling the loop on purpose.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken, bool preserveWantedLoop = false);

    /// <summary>
    /// Strict teardown used by explicit unmonitoring. A recorded client item must be confirmed absent or
    /// removed with its data before the acquisition row is hard-deleted; client/config failures leave the
    /// acquisition durable for a retry.
    /// </summary>
    Task<bool> DeleteForUnmonitorAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an imported acquisition whose owned files were deliberately deleted with a clean retry,
    /// re-points its monitor, and starts a release search immediately. The linked entity must already be a
    /// fileless wanted placeholder; returns the replacement acquisition id. If the clean clone cannot be
    /// created, the now-invalid imported row and its per-item monitor are removed so callers can never retain
    /// an Imported state for files that no longer exist, and null is returned. This is intentionally separate
    /// from <see cref="DeleteAsync"/> so removing a row from Downloads keeps its normal monitor cadence instead
    /// of immediately re-grabbing it.
    /// </summary>
    Task<Guid?> ReacquireAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Read-only verdict for replacing one imported acquisition with a clean search after its files are deleted.
/// </summary>
/// <param name="CanReacquire">True only when the acquisition is currently safe to supersede.</param>
/// <param name="Message">Actionable reason when reacquisition is not currently safe.</param>
public sealed record AcquisitionReacquireEligibility(bool CanReacquire, string? Message = null);

/// <summary>Read-only verdict for safely cancelling and hard-deleting one acquisition.</summary>
/// <param name="CanRemove">Whether removal can proceed without crossing an active/partial import.</param>
/// <param name="Message">Actionable reason when the acquisition must finish recovery first.</param>
public sealed record AcquisitionRemovalEligibility(bool CanRemove, string? Message = null);

/// <summary>
/// Cancels durable acquisition jobs by their exact acquisition target before the acquisition row is
/// removed. This keeps queued/running work from remaining visible or acting on a deleted lifecycle.
/// </summary>
public interface IAcquisitionJobCleanup {
    /// <summary>Cancels queued and running jobs whose target is exactly <paramref name="acquisitionId"/>.</summary>
    Task<int> CancelAsync(Guid acquisitionId, CancellationToken cancellationToken);
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
    IMonitorStore monitors,
    IAcquisitionJobCleanup acquisitionJobs,
    IEntityLifecycleMutationLease entityLifecycle,
    VideoScanConcurrencyGate? scanGate = null) : IAcquisitionRequestService {
    public Task<IReadOnlyList<AcquisitionSummary>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public Task<AcquisitionDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        store.GetAsync(id, cancellationToken);

    /// <summary>The latest acquisition backing a library entity (wanted or imported), or null when it has none.</summary>
    public Task<AcquisitionDetail?> GetForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        store.GetLatestForEntityAsync(entityId, cancellationToken);

    /// <inheritdoc />
    public async Task<AcquisitionRemovalEligibility> GetRemovalEligibilityAsync(
        Guid id,
        CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            return new AcquisitionRemovalEligibility(true);
        }

        if (detail.Summary.Status == AcquisitionStatus.Stopping) {
            var claim = await store.GetTeardownClaimAsync(id, cancellationToken);
            return claim?.Intent == AcquisitionTeardownIntent.Remove
                ? new AcquisitionRemovalEligibility(true)
                : new AcquisitionRemovalEligibility(
                    false,
                    "This acquisition is already stopping for a different cleanup operation.");
        }

        if (detail.Summary.Status == AcquisitionStatus.Importing) {
            return new AcquisitionRemovalEligibility(false, ActiveImportConflict(detail.Summary.Status).Message);
        }

        if (detail.Summary.Status == AcquisitionStatus.Queued
            && (await store.GetTransferInfoAsync(id, cancellationToken))?.ClientItemId is not { Length: > 0 }) {
            return new AcquisitionRemovalEligibility(
                false,
                "This acquisition is still being handed to its download client. Wait for queueing to finish and retry.");
        }

        AcquisitionImportContext? import;
        try {
            import = await store.GetImportContextAsync(id, cancellationToken);
        } catch (InvalidDataException) {
            return new AcquisitionRemovalEligibility(false, ImportCheckpointLifecycle.CorruptCheckpointMessage);
        }

        if (import is not ({ TvImportCheckpoint: not null } or { ImportPlacementCheckpoint: not null })
            || await ImportCheckpointLifecycle.CanAbandonAsync(import, cancellationToken, scanGate)) {
            return new AcquisitionRemovalEligibility(true);
        }

        return new AcquisitionRemovalEligibility(false, ImportCheckpointLifecycle.CheckpointMustFinishMessage);
    }

    /// <inheritdoc />
    public async Task<AcquisitionReacquireEligibility> GetReacquireEligibilityAsync(
        Guid id,
        CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        return detail is null
            ? new AcquisitionReacquireEligibility(false, "The acquisition no longer exists.")
            : await EvaluateReacquireEligibilityAsync(detail, cancellationToken);
    }

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

        if (detail.Summary.Status is AcquisitionStatus.Importing or AcquisitionStatus.Imported) {
            throw ActiveImportConflict(detail.Summary.Status);
        }

        await EnsureImportCheckpointCanBeSupersededAsync(detail, cancellationToken);
        if (!await store.TryTransitionStatusAsync(
                id,
                [detail.Summary.Status],
                AcquisitionStatus.Cancelled,
                "Cancelled.",
                cancellationToken)) {
            throw LifecycleChangedConflict();
        }

        await RemoveTransferDataAsync(id, cancellationToken);

        await RecordRemovedAsync(detail.Summary, "Cancelled by user.", cancellationToken);

        // Cancel stops THIS download only — the wanted placeholder and any monitor stay exactly as they
        // are (monitoring is managed separately). An active monitor re-searches the cancelled acquisition
        // on its normal cadence; the explicit give-up paths are the grid's "Remove wanted" and the
        // entity's delete/unmonitor actions.
        return await store.GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Removes an acquisition (the download pipeline record): best-effort deletes its download (and data)
    /// from the owning client, then hard-deletes the record. The wanted placeholder entity is KEPT —
    /// removing a download cleans up the transfer, it does not un-want the item; the grid's
    /// "Remove wanted" and the entity's delete/unmonitor actions are the explicit give-up paths.
    /// With <paramref name="preserveWantedLoop"/> (the user-facing Downloads remove), a monitor watching
    /// the removed acquisition survives: the record is cloned into a fresh pending acquisition for the
    /// same wanted entity and the monitor re-pointed at it, so the loop re-searches on its normal
    /// cadence instead of orphan-pausing. Internal teardown paths (remove-wanted, entity deletion) leave
    /// the flag off — they are dismantling the loop on purpose.
    /// </summary>
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken, bool preserveWantedLoop = false) =>
        DeleteCoreAsync(id, preserveWantedLoop, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteForUnmonitorAsync(Guid id, CancellationToken cancellationToken) {
        if (!await ClaimTeardownAsync(id, AcquisitionTeardownIntent.Remove, cancellationToken)) {
            return false;
        }

        await ConfirmTransferRemovedAsync(id, cancellationToken);
        return await CompleteTeardownAsync(id, AcquisitionTeardownIntent.Remove, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ClaimTeardownAsync(
        Guid id,
        AcquisitionTeardownIntent intent,
        CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            return false;
        }

        if (detail.Summary.Status == AcquisitionStatus.Stopping) {
            var existing = await store.GetTeardownClaimAsync(id, cancellationToken);
            if (existing?.Intent != intent) {
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "This acquisition is already stopping for a different cleanup operation.");
            }

            await acquisitionJobs.CancelAsync(id, cancellationToken);
            return true;
        }

        if (intent == AcquisitionTeardownIntent.Reacquire) {
            var eligibility = await EvaluateReacquireEligibilityAsync(detail, cancellationToken);
            if (!eligibility.CanReacquire) {
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    eligibility.Message ?? "This acquisition cannot be reacquired right now.");
            }
        } else {
            var eligibility = await GetRemovalEligibilityAsync(id, cancellationToken);
            if (!eligibility.CanRemove) {
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    eligibility.Message ?? "This acquisition cannot be removed right now.");
            }
        }

        await EnsureImportCheckpointCanBeSupersededAsync(detail, cancellationToken);
        if (!await store.TryClaimTeardownAsync(
                id,
                detail.Summary.Status,
                intent,
                intent == AcquisitionTeardownIntent.Reacquire
                    ? "Removing files before reacquisition."
                    : "Removing acquisition.",
                cancellationToken)) {
            throw LifecycleChangedConflict();
        }

        // Job cancellation is part of the durable local claim and precedes the first remote/filesystem
        // effect. A failure leaves Stopping durable; retry repeats exact cancellation idempotently.
        await acquisitionJobs.CancelAsync(id, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public Task ConfirmTransferRemovedAsync(Guid id, CancellationToken cancellationToken) =>
        RemoveTransferDataStrictAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> CompleteTeardownAsync(
        Guid id,
        AcquisitionTeardownIntent intent,
        CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            return false;
        }

        var claim = await store.GetTeardownClaimAsync(id, cancellationToken);
        if (claim?.Intent != intent) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "The acquisition no longer carries the expected cleanup claim. Refresh and retry.");
        }

        await RecordRemovedAsync(detail.Summary, "Removed by user.", cancellationToken);
        return await store.DeleteAsync(id, cancellationToken);
    }

    private async Task<bool> DeleteCoreAsync(
        Guid id,
        bool preserveWantedLoop,
        CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        var summary = detail?.Summary;
        if (detail is null) {
            return false;
        }

        if (detail.Summary.Status == AcquisitionStatus.Stopping) {
            var claim = await store.GetTeardownClaimAsync(id, cancellationToken);
            var expectedIntent = preserveWantedLoop
                ? AcquisitionTeardownIntent.Reacquire
                : AcquisitionTeardownIntent.Remove;
            if (claim?.Intent != expectedIntent) {
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "This acquisition is already stopping. Retry the operation that started its cleanup.");
            }
        } else if (preserveWantedLoop) {
            await EnsureImportCheckpointCanBeSupersededAsync(detail, cancellationToken);
            if (!await store.TryClaimTeardownAsync(
                    id,
                    detail.Summary.Status,
                    AcquisitionTeardownIntent.Reacquire,
                    "Removing download and restarting its wanted search.",
                    cancellationToken)) {
                throw LifecycleChangedConflict();
            }
        } else {
            await ClaimTeardownAsync(id, AcquisitionTeardownIntent.Remove, cancellationToken);
        }

        if (preserveWantedLoop || detail.Summary.Status == AcquisitionStatus.Stopping) {
            await acquisitionJobs.CancelAsync(id, cancellationToken);
        }
        // Every path below hard-deletes or supersedes the only durable transfer pointer. Prove the remote
        // item absent first; an outage leaves this Stopping row and its pointer retryable instead of
        // orphaning client data.
        await RemoveTransferDataStrictAsync(id, cancellationToken);

        if (preserveWantedLoop) {
            // The replacement is durably elected and linked from the Stopping owner before retargeting.
            // A crash anywhere after this point resumes the same clone instead of manufacturing duplicate
            // Pending rows on each retry of Downloads → Remove.
            var replacementId = await store.GetOrCreateTeardownReplacementAsync(id, cancellationToken);
            if (replacementId is { } freshId) {
                if (!await monitors.RetargetAsync(id, freshId, cancellationToken)) {
                    // A recursive unmonitor claim won the race after the clone was created. The clone has
                    // no job or transfer yet, so remove it immediately.
                    await store.DeleteAsync(freshId, cancellationToken);
                } else {
                    var replacementStatus = await store.GetStatusAsync(freshId, cancellationToken);
                    if (replacementStatus == AcquisitionStatus.Pending
                        && await store.TryTransitionStatusAsync(
                            freshId,
                            [AcquisitionStatus.Pending],
                            AcquisitionStatus.Searching,
                            null,
                            cancellationToken)) {
                        replacementStatus = AcquisitionStatus.Searching;
                    } else if (replacementStatus == AcquisitionStatus.Pending) {
                        replacementStatus = await store.GetStatusAsync(freshId, cancellationToken);
                    }

                    if (replacementStatus == AcquisitionStatus.Searching
                        && !await queue.HasPendingAsync(
                            JobType.AcquisitionSearch,
                            freshId.ToString(),
                            cancellationToken)) {
                        await queue.EnqueueAsync(
                            new EnqueueJobRequest(
                                JobType.AcquisitionSearch,
                                PayloadJson: AcquisitionJobPayload.Serialize(freshId),
                                TargetEntityId: freshId.ToString(),
                                TargetLabel: detail.Summary.Title),
                            cancellationToken);
                    }
                }
            }

            if (summary is not null) {
                await RecordRemovedAsync(summary, "Removed by user.", cancellationToken);
            }
        }

        return preserveWantedLoop
            ? await store.DeleteAsync(id, cancellationToken)
            : await CompleteTeardownAsync(id, AcquisitionTeardownIntent.Remove, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Guid?> ReacquireAsync(Guid id, CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            return null;
        }

        await ClaimTeardownAsync(id, AcquisitionTeardownIntent.Reacquire, cancellationToken);
        // A direct call claims before remote work; managed Entity deletion reaches this method with the same
        // durable claim after disk succeeds. Confirming absence again is intentionally idempotent on retry.
        await ConfirmTransferRemovedAsync(id, cancellationToken);

        // The replacement and its pointer from the teardown owner are committed together. A retry therefore
        // resumes this exact clean lifecycle instead of creating a second acquisition after a crash between
        // monitor retargeting, search initialization, and removal of the old Imported row.
        var replacementId = await store.GetOrCreateTeardownReplacementAsync(id, cancellationToken);
        if (replacementId is not { } freshId) {
            // The entity is already fileless + Wanted, so preserving an Imported acquisition here would be
            // actively false and every action against it would target a dead lifecycle. Remove its helper
            // monitor and row; the surviving container monitor (if any) can still rediscover the wanted gap.
            if (await monitors.GetByAcquisitionAsync(id, cancellationToken) is { } staleMonitor) {
                await monitors.DeleteAsync(staleMonitor.Id, cancellationToken);
            }

            await RecordRemovedAsync(
                detail.Summary, "Files deleted; retry could not be initialized.", cancellationToken);
            await store.DeleteAsync(id, cancellationToken);
            return null;
        }

        if (!await monitors.RetargetAfterFileDeletionAsync(id, freshId, cancellationToken)
            && !await monitors.RetargetAsync(id, freshId, cancellationToken)) {
            // Claim-first race: never leave the pending clone outside the immutable unmonitor scope.
            // Remote data was already confirmed absent before any local lifecycle transition.
            await store.DeleteAsync(freshId, cancellationToken);
            await RecordRemovedAsync(
                detail.Summary, "Files deleted while monitoring was being stopped.", cancellationToken);
            await store.DeleteAsync(id, cancellationToken);
            return null;
        }

        var replacementStatus = await store.GetStatusAsync(freshId, cancellationToken);
        if (replacementStatus == AcquisitionStatus.Pending) {
            if (await store.TryTransitionStatusAsync(
                    freshId,
                    [AcquisitionStatus.Pending],
                    AcquisitionStatus.Searching,
                    null,
                    cancellationToken)) {
                replacementStatus = AcquisitionStatus.Searching;
            } else {
                replacementStatus = await store.GetStatusAsync(freshId, cancellationToken);
            }
        }

        // Searching is the durable intent. A retry closes either side of the status/enqueue crash window,
        // while the queue lookup prevents a second search job after retarget or history work is retried.
        if (replacementStatus == AcquisitionStatus.Searching
            && !await queue.HasPendingAsync(
                JobType.AcquisitionSearch,
                freshId.ToString(),
                cancellationToken)) {
            await queue.EnqueueAsync(
                new EnqueueJobRequest(
                    JobType.AcquisitionSearch,
                    PayloadJson: AcquisitionJobPayload.Serialize(freshId),
                    TargetEntityId: freshId.ToString(),
                    TargetLabel: detail.Summary.Title),
                cancellationToken);
        }

        // Record while the old FK target still exists; the history row survives its deletion via SetNull.
        await RecordRemovedAsync(detail.Summary, "Files deleted; searching again.", cancellationToken);
        await store.DeleteAsync(id, cancellationToken);
        return freshId;
    }

    private async Task<AcquisitionReacquireEligibility> EvaluateReacquireEligibilityAsync(
        AcquisitionDetail detail,
        CancellationToken cancellationToken) {
        if (detail.Summary.Status == AcquisitionStatus.Stopping) {
            var claim = await store.GetTeardownClaimAsync(detail.Summary.Id, cancellationToken);
            return claim is {
                Intent: AcquisitionTeardownIntent.Reacquire,
                OriginalStatus: AcquisitionStatus.Imported
            }
                ? new AcquisitionReacquireEligibility(true)
                : new AcquisitionReacquireEligibility(
                    false,
                    "This acquisition is already stopping for a different cleanup operation.");
        }

        if (detail.Summary.Status != AcquisitionStatus.Imported) {
            return new AcquisitionReacquireEligibility(
                false,
                ActiveImportConflict(detail.Summary.Status).Message);
        }

        AcquisitionImportContext? import;
        try {
            import = await store.GetImportContextAsync(detail.Summary.Id, cancellationToken);
        } catch (InvalidDataException) {
            return new AcquisitionReacquireEligibility(false, ImportCheckpointLifecycle.CorruptCheckpointMessage);
        }

        if (import is not ({ TvImportCheckpoint: not null } or { ImportPlacementCheckpoint: not null })
            || await ImportCheckpointLifecycle.CanAbandonAsync(
                import,
                cancellationToken,
                scanGate)) {
            return new AcquisitionReacquireEligibility(true);
        }

        return new AcquisitionReacquireEligibility(false, ImportCheckpointLifecycle.CheckpointMustFinishMessage);
    }

    /// <summary>
    /// Best-effort removal of an acquisition's recorded download-client item and its data. A missing client
    /// item is already the desired end state, while cancellation still propagates to the caller.
    /// </summary>
    private async Task RemoveTransferDataAsync(Guid id, CancellationToken cancellationToken) {
        var transfer = await store.GetTransferInfoAsync(id, cancellationToken);
        if (string.IsNullOrWhiteSpace(transfer?.ClientItemId)) {
            return;
        }

        var client = await ResolveRemovalClientAsync(transfer, cancellationToken);
        if (client is null) {
            return;
        }

        try {
            await clients.Get(client.Kind).RemoveAsync(
                ConnectionFor(client), transfer.ClientItemId, deleteData: true, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception) {
            // The download may already be gone; local cancellation/removal/reacquisition must still finish.
        }
    }

    /// <summary>
    /// Strict counterpart used when unmonitoring promises no orphan download. A missing completed item is
    /// already success; an unresolved pre-Add handoff or unavailable client fails closed with its pointer intact.
    /// </summary>
    private async Task RemoveTransferDataStrictAsync(Guid id, CancellationToken cancellationToken) {
        var transfer = await store.GetTransferInfoAsync(id, cancellationToken);
        if (string.IsNullOrWhiteSpace(transfer?.ClientItemId)) {
            return;
        }

        var client = await ResolveRemovalClientAsync(transfer, cancellationToken);
        if (client is null) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "The recorded download client is unavailable, so its transfer could not be removed. Restore the client configuration and retry.");
        }

        try {
            var download = clients.Get(client.Kind);
            var connection = ConnectionFor(client) with {
                Category = transfer.Category ?? client.Category
            };
            var clientItemId = transfer.ClientItemId;
            var item = await download.GetItemAsync(connection, clientItemId, cancellationToken);
            if (item is null && transfer.State == TransferOwnershipState.Adding.ToCode()) {
                var matches = (await download.ListItemsAsync(connection, cancellationToken))
                    .Where(candidate => DownloadAddCorrelation.MatchesName(clientItemId, candidate.Name))
                    .GroupBy(candidate => candidate.ClientItemId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToArray();
                if (matches.Length > 1) {
                    throw new AcquisitionConfigurationException(
                        ApiProblemCodes.AcquisitionInvalid,
                        "The unfinished download handoff matches multiple client items in its category. Prismedia will not guess which data to delete; remove the duplicate manually and retry cleanup.");
                }

                item = matches.SingleOrDefault();
                if (item is null) {
                    throw new AcquisitionConfigurationException(
                        ApiProblemCodes.AcquisitionInvalid,
                        "The unfinished download handoff could not be resolved in its recorded client category. Retry the same queue action or remove the client item manually; Prismedia will keep its ownership pointer until cleanup can be proven complete.");
                }
                clientItemId = item.ClientItemId;
            } else if (item is null) {
                return;
            }

            await download.RemoveAsync(connection, clientItemId, deleteData: true, cancellationToken);
            if (await download.GetItemAsync(connection, clientItemId, cancellationToken) is not null) {
                throw new IOException("The transfer is still present after the client acknowledged removal.");
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (AcquisitionConfigurationException) {
            throw;
        } catch (Exception exception) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                $"The download client could not remove the active transfer: {exception.Message}");
        }
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
    /// re-checks that the acquisition is still searchable — so it can't disturb an in-flight grab. An explicit
    /// user action may revive Cancelled by claiming Searching before enqueue; stale queued jobs cannot. Returns
    /// the acquisition, or null when it no longer exists.
    /// </summary>
    public async Task<AcquisitionDetail?> ReSearchAsync(Guid id, CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            return null;
        }

        var explicitRevival = detail.Summary.Status == AcquisitionStatus.Cancelled;
        if (!explicitRevival && !AcquisitionSearchJobHandler.CanScheduleSearch(detail.Summary.Status)) {
            return detail;
        }

        await EnsureImportCheckpointCanBeSupersededAsync(detail, cancellationToken);
        if (!await store.TryTransitionStatusAsync(
                id,
                [detail.Summary.Status],
                AcquisitionStatus.Searching,
                null,
                cancellationToken)) {
            return await store.GetAsync(id, cancellationToken);
        }

        await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionSearch,
                PayloadJson: AcquisitionJobPayload.Serialize(id),
                TargetEntityId: id.ToString(),
                TargetLabel: detail.Summary.Title),
            cancellationToken);
        return await store.GetAsync(id, cancellationToken);
    }

    private async Task EnsureImportCheckpointCanBeSupersededAsync(
        AcquisitionDetail detail,
        CancellationToken cancellationToken) {
        if (detail.Summary.Status == AcquisitionStatus.Importing) {
            throw ActiveImportConflict(detail.Summary.Status);
        }

        AcquisitionImportContext? import;
        try {
            import = await store.GetImportContextAsync(detail.Summary.Id, cancellationToken);
        } catch (InvalidDataException) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                ImportCheckpointLifecycle.CorruptCheckpointMessage);
        }

        if (import is { TvImportCheckpoint: not null } or { ImportPlacementCheckpoint: not null }
            && !await ImportCheckpointLifecycle.TryAbandonAsync(
                store,
                import,
                cancellationToken,
                scanGate)) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                ImportCheckpointLifecycle.CheckpointMustFinishMessage);
        }
    }

    private static AcquisitionConfigurationException ActiveImportConflict(AcquisitionStatus status) =>
        new(
            ApiProblemCodes.AcquisitionInvalid,
            $"This acquisition cannot be changed while it is {status.ToCode()}. Refresh after the current operation finishes.");

    private static AcquisitionConfigurationException LifecycleChangedConflict() =>
        new(
            ApiProblemCodes.AcquisitionInvalid,
            "The acquisition changed while this action was being prepared. Refresh and try again.");

    /// <summary>
    /// Re-runs the import for a downloaded or manual-import-held acquisition on demand — the hold's
    /// "Import anyway". <paramref name="allowFormatChange"/> carries the user's explicit consent for a
    /// genuine upgrade to replace the owned file across formats (e.g. mkv → mp4, recycling the old
    /// file); without it the import re-runs under the ordinary rules. Any other status returns the
    /// detail unchanged (nothing enqueued); null when the acquisition no longer exists. The
    /// dangerous-file hold is enforced by the import job regardless of the flag.
    /// </summary>
    public async Task<AcquisitionDetail?> RetryImportAsync(Guid id, bool allowFormatChange, CancellationToken cancellationToken) {
        var detail = await store.GetAsync(id, cancellationToken);
        if (detail is null) {
            return null;
        }

        if (detail.Summary.Status is not (AcquisitionStatus.Downloaded or AcquisitionStatus.ManualImportRequired)
            && !(detail.Summary.Status == AcquisitionStatus.Failed && detail.Summary.HasResumableImport)) {
            return detail;
        }

        await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionImport,
                PayloadJson: AcquisitionJobPayload.Serialize(id, allowFormatChange, manualRetry: true),
                TargetEntityId: id.ToString(),
                TargetLabel: detail.Summary.Title),
            cancellationToken);
        return detail;
    }

    /// <summary>
    /// Persists a new acquisition and schedules its search. Entity-bound work holds the authoritative
    /// Entity/monitor lifecycle lease across persistence, Searching provenance, and queue publication.
    /// Unbound work preserves the ordinary acquisition-only path.
    /// </summary>
    public async Task<AcquisitionSummary> CreateAndSearchAsync(AcquisitionCreateRequest request, CancellationToken cancellationToken) {
        var metadata = CreateMetadata(request);
        if (metadata.EntityId is not { } entityId) {
            return await CreateAndSearchCoreAsync(metadata, cancellationToken);
        }

        AcquisitionSummary? summary = null;
        var accepted = await entityLifecycle.ExecuteAsync(
            entityId,
            async leaseCancellationToken => summary = await CreateAndSearchCoreAsync(
                metadata,
                leaseCancellationToken),
            cancellationToken);
        if (!accepted || summary is null) {
            throw EntityLifecycleConflict();
        }

        return summary;
    }

    /// <inheritdoc />
    public Task<AcquisitionSummary> CreateAndSearchWithinEntityLifecycleAsync(
        AcquisitionCreateRequest request,
        CancellationToken cancellationToken) =>
        CreateAndSearchCoreAsync(CreateMetadata(request), cancellationToken);

    private static AcquisitionMetadata CreateMetadata(AcquisitionCreateRequest request) {
        if (string.IsNullOrWhiteSpace(request.Title)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionInvalid, "A title is required to start an acquisition.");
        }

        var externalIdentity = CreateExternalIdentity(request);
        return new AcquisitionMetadata(
            request.Title.Trim(),
            string.IsNullOrWhiteSpace(request.Author) ? null : request.Author.Trim(),
            string.IsNullOrWhiteSpace(request.Series) ? null : request.Series.Trim(),
            request.Year,
            string.IsNullOrWhiteSpace(request.PosterUrl) ? null : request.PosterUrl.Trim(),
            externalIdentity,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.Kind,
            request.EntityId,
            request.ProfileId,
            request.TargetLibraryRootId,
            request.SeasonNumber,
            request.EpisodeNumber,
            request.VolumeNumber);
    }

    private async Task<AcquisitionSummary> CreateAndSearchCoreAsync(
        AcquisitionMetadata metadata,
        CancellationToken cancellationToken) {
        var summary = await store.CreateAsync(metadata, cancellationToken);
        if (!await store.TryTransitionStatusAsync(
                summary.Id,
                [AcquisitionStatus.Pending],
                AcquisitionStatus.Searching,
                null,
                cancellationToken)) {
            throw LifecycleChangedConflict();
        }
        summary = summary with {
            Status = AcquisitionStatus.Searching,
            StatusMessage = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await queue.EnqueueAsync(
            new EnqueueJobRequest(
                JobType.AcquisitionSearch,
                PayloadJson: AcquisitionJobPayload.Serialize(summary.Id),
                TargetEntityId: summary.Id.ToString(),
                TargetLabel: summary.Title),
            cancellationToken);

        // When the request carries a persistent external identity, enrich the held metadata in the background
        // through the plugin registered for its namespace (cover, fuller description, dates the lightweight
        // search result lacked), so the acquisition surface fills in and the imported book can be seeded.
        // Best-effort — never blocks the request.
        if (metadata.ExternalIdentity is not null) {
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

    private static AcquisitionConfigurationException EntityLifecycleConflict() =>
        new(
            ApiProblemCodes.AcquisitionInvalid,
            "This Entity is missing or still being changed by another cleanup operation. Refresh and retry the request.");

    private static ExternalIdentity? CreateExternalIdentity(AcquisitionCreateRequest request) {
        var hasNamespace = !string.IsNullOrWhiteSpace(request.IdentityNamespace);
        var hasValue = !string.IsNullOrWhiteSpace(request.IdentityValue);
        if (!hasNamespace && !hasValue) {
            return null;
        }

        if (!hasNamespace || !hasValue) {
            throw new AcquisitionConfigurationException(
                ApiProblemCodes.AcquisitionInvalid,
                "An external identity requires both a namespace and a value.");
        }

        try {
            return new ExternalIdentity(request.IdentityNamespace!, request.IdentityValue!);
        } catch (ArgumentException exception) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionInvalid, exception.Message);
        }
    }

    /// <inheritdoc />
    public Task<bool> AnyOpenForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
        store.AnyOpenForEntityAsync(entityId, cancellationToken);

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
