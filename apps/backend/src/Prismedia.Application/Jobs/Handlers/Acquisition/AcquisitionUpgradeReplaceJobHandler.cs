using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Applies a fully-downloaded upgrade child to the book it upgrades: re-confirms the downloaded release is
/// still a strict improvement, atomically swaps the better file in for the owned one (keeping the original as
/// a backup), updates the parent's owned quality, refreshes the book via a re-scan, cleans up the torrent, and
/// releases the monitor's upgrade slot. The owned file is the only library file mutated, and only after every
/// gate passes; any failure leaves the owned book untouched and surfaces the download for manual handling.
/// </summary>
public sealed class AcquisitionUpgradeReplaceJobHandler(
    IAcquisitionStore acquisitions,
    IMonitorStore monitors,
    IOwnedFileReplacer replacer,
    IDownloadClientConfigStore downloadClients,
    IDownloadClientFactory clients,
    ILogger<AcquisitionUpgradeReplaceJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.AcquisitionUpgradeReplace;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = AcquisitionJobPayload.Parse(context.Job.PayloadJson);
        var childId = payload.AcquisitionId;

        var target = await acquisitions.GetUpgradeReplaceTargetAsync(childId, cancellationToken);
        if (target is null) {
            logger.LogInformation("AcquisitionUpgradeReplace: {Child} is not a resolvable upgrade child; skipping.", childId);
            return;
        }

        if (string.IsNullOrWhiteSpace(target.ParentFinalSourcePath) || string.IsNullOrWhiteSpace(target.ChildContentPath)) {
            await AbortAsync(childId, "The owned book location or the upgrade download path is unknown.", cancellationToken);
            return;
        }

        // The downloaded child must carry the release it grabbed; without it we cannot judge the upgrade.
        if (string.IsNullOrWhiteSpace(target.ChildSelectedTitle)) {
            await AbortAsync(childId, "The upgrade release information is missing.", cancellationToken);
            return;
        }

        // Last gate before touching the file: re-confirm the downloaded release still strictly beats the
        // parent's CURRENT owned quality (it may have changed since the search). The format axis is re-checked
        // against the actual file inside the replacer; here we guard overall dominance from the release title.
        var candidate = BookFormatDetection.DetectQuality(target.ChildSelectedTitle);
        if (!candidate.StrictlyDominates(target.ParentOwnedQuality)) {
            await AbortAsync(childId, "The downloaded release is no longer an upgrade over the current copy.", cancellationToken);
            return;
        }

        var result = await replacer.ReplaceAsync(target.ParentFinalSourcePath, target.ChildContentPath, target.ParentOwnedQuality.Format, cancellationToken);
        if (!result.Succeeded) {
            await AbortAsync(childId, result.FailureReason ?? "The upgrade could not be applied.", cancellationToken);
            return;
        }

        // The parent now owns the better file. Record its new owned quality FIRST (source from the release
        // title, format from the installed file): this is the load-bearing bookkeeping for the upgrade loop, so
        // doing it before the best-effort cleanup means a crash mid-completion leaves the loop seeing the
        // upgrade rather than retrying it. Then refresh metadata via a re-scan, clean up the torrent, release
        // the upgrade slot (counting the attempt), and remove the now-consumed child acquisition.
        var newOwned = new BookQualityRank(BookFormatDetection.DetectSource(target.ChildSelectedTitle), result.NewFormat);
        await acquisitions.UpdateOwnedQualityAsync(target.ParentId, newOwned, cancellationToken);
        await monitors.ResolveUpgradeChildAsync(childId, succeeded: true, cancellationToken);
        await context.EnqueueIfNeededAsync(new EnqueueJobRequest(JobType.ScanBook, TargetLabel: "Upgraded book scan"), cancellationToken);
        await RemoveTorrentAsync(target, cancellationToken);
        await acquisitions.DeleteAsync(childId, cancellationToken);
        logger.LogInformation("AcquisitionUpgradeReplace: upgraded acquisition {Parent} via child {Child}.", target.ParentId, childId);
    }

    /// <summary>Records the upgrade attempt as failed: marks the child failed (so it stays visible) and releases the monitor's slot, counting it as barren.</summary>
    private async Task AbortAsync(Guid childId, string reason, CancellationToken cancellationToken) {
        logger.LogInformation("AcquisitionUpgradeReplace: not applying child {Child}: {Reason}", childId, reason);
        await acquisitions.SetStatusAsync(childId, AcquisitionStatus.Failed, reason, cancellationToken);
        await monitors.ResolveUpgradeChildAsync(childId, succeeded: false, cancellationToken);
    }

    private async Task RemoveTorrentAsync(UpgradeReplaceTarget target, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(target.ChildClientItemId)) {
            return;
        }

        var client = target.ChildDownloadClientConfigId is { } id
            ? await downloadClients.GetAsync(id, cancellationToken) ?? await downloadClients.GetDefaultAsync(cancellationToken)
            : await downloadClients.GetDefaultAsync(cancellationToken);
        if (client is null) {
            return;
        }

        try {
            var connection = new DownloadClientConnection(client.Id, client.Kind, client.BaseUrl, client.Username, client.Password, client.Category);
            await clients.Get(client.Kind).RemoveAsync(connection, target.ChildClientItemId, deleteData: true, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            // The book is already upgraded; a torrent-cleanup failure must not undo that.
            logger.LogWarning(ex, "AcquisitionUpgradeReplace: failed to remove the upgrade torrent for parent {Parent}.", target.ParentId);
        }
    }
}
