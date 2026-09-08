using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Persists retained TV retry observations alongside the acquisition's durable completion state.</summary>
public sealed partial class EfHeldTvImportRecoveryStore(PrismediaDbContext db, IJobQueueService queue) : IHeldTvImportRecoveryStore {
    /// <inheritdoc />
    public async Task<IReadOnlyList<HeldTvImport>> ListAsync(CancellationToken cancellationToken) {
        await RestoreRecordedAutomaticAuthorityAsync(cancellationToken);
        var held = await WithCompletedPayload().AsNoTracking()
            .Where(row => row.Status == AcquisitionStatus.ManualImportRequired
                && (row.Kind == EntityKind.VideoSeason || row.Kind == EntityKind.VideoEpisode)
                && row.EntityId != null && !row.ImportManualReview
                && row.ImportClaimJobId == null
                && row.UpgradeOfAcquisitionId == null)
            .OrderBy(row => row.UpdatedAt)
            .Select(row => new {
                Row = row,
                Transfer = db.DownloadTransfers.Where(transfer => transfer.AcquisitionId == row.Id)
                    .OrderByDescending(transfer => transfer.CreatedAt).First()
            })
            .ToArrayAsync(cancellationToken);
        return held.Select(item => new HeldTvImport(item.Row.Id, item.Row.EntityId!.Value,
            item.Row.UpdatedAt, item.Row.ImportRecoveryFingerprint, item.Row.FinalSourcePath, item.Row.ImportResultJson,
            item.Row.ImportCheckpointJson, item.Row.SelectedReleaseJson,
            item.Transfer.Id, item.Transfer.ContentPath, item.Transfer.ClientItemId)).Where(CanReconsiderPayload).ToArray();
    }

    /// <inheritdoc />
    public async Task RequestMetadataRefreshAsync(HeldTvImport held, CancellationToken cancellationToken) {
        if (!CanReconsiderPayload(held) || held.CheckpointSnapshot is not null) return;
        var target = held.Id.ToString();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        // A completed provider miss is still an attempt. Persist the cooldown in job history so
        // another worker scope or restart cannot repeatedly refresh an unchanged incomplete catalog.
        if (await db.JobRuns.AnyAsync(job => job.Type == JobType.AcquisitionEnrich
                && job.TargetEntityId == target
                && (job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running
                    || job.CreatedAt >= cutoff || job.FinishedAt >= cutoff), cancellationToken)) return;
        var current = await WithObservedPayload(held).AsNoTracking()
            .Where(row => row.Id == held.Id && row.EntityId == held.EntityId
                && row.Status == AcquisitionStatus.ManualImportRequired && row.UpdatedAt == held.HeldAt
                && !row.ImportManualReview && row.ImportClaimJobId == null && row.UpgradeOfAcquisitionId == null
                && row.SelectedReleaseJson == held.SelectedReleaseSnapshot
                && row.ImportCheckpointJson == held.CheckpointSnapshot
                && row.ImportResultJson == held.ImportResultSnapshot && row.FinalSourcePath == held.FinalSourcePath
                && row.IdentityNamespace != null && row.IdentityValue != null
                && db.Monitors.Any(monitor => monitor.EntityId == held.EntityId && monitor.Status == MonitorStatus.Active))
            .Select(row => new { row.Title, row.Kind }).SingleOrDefaultAsync(cancellationToken);
        if (current is null) return;
        await queue.EnqueueAsync(new EnqueueJobRequest(JobType.AcquisitionEnrich,
            PayloadJson: AcquisitionJobPayload.Serialize(held.Id), TargetEntityId: target,
            TargetLabel: current.Title, Origin: JobGraphOrigin.Background,
            GraphRootEntityKind: current.Kind.ToCode(), GraphRootEntityId: held.EntityId.ToString()), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryResumeAsync(HeldTvImport held, string fingerprint, CancellationToken cancellationToken) {
        if (!CanReconsiderPayload(held)) return false;
        if (db.Database.IsRelational()) {
            var affected = await WithObservedPayload(held)
                .Where(row => row.Id == held.Id && row.Status == AcquisitionStatus.ManualImportRequired
                    && row.UpdatedAt == held.HeldAt && row.EntityId == held.EntityId
                    && row.ImportRecoveryFingerprint != fingerprint && !row.ImportManualReview
                    && row.ImportCheckpointJson == held.CheckpointSnapshot && row.FinalSourcePath == held.FinalSourcePath
                    && row.SelectedReleaseJson == held.SelectedReleaseSnapshot && row.ImportClaimJobId == null
                    && row.ImportResultJson == held.ImportResultSnapshot && row.UpgradeOfAcquisitionId == null
                    && (row.Kind == EntityKind.VideoSeason || row.Kind == EntityKind.VideoEpisode))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.ImportRecoveryFingerprint, fingerprint)
                    .SetProperty(row => row.Status, AcquisitionStatus.Downloaded)
                    .SetProperty(row => row.StatusMessage, ResumeMessage)
                    .SetProperty(row => row.ImportClaimJobId, (Guid?)null)
                    .SetProperty(row => row.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
            if (affected == 1 && db.ChangeTracker.Entries<Persistence.Entities.AcquisitionRow>()
                    .FirstOrDefault(entry => entry.Entity.Id == held.Id) is { } tracked) {
                await tracked.ReloadAsync(cancellationToken);
            }
            return affected == 1;
        }
        var row = await WithObservedPayload(held).FirstOrDefaultAsync(row => row.Id == held.Id, cancellationToken);
        if (row is null || row.Status != AcquisitionStatus.ManualImportRequired || row.UpdatedAt != held.HeldAt
            || row.EntityId != held.EntityId || row.ImportRecoveryFingerprint == fingerprint || row.ImportManualReview
            || row.ImportCheckpointJson != held.CheckpointSnapshot || row.FinalSourcePath != held.FinalSourcePath
            || row.SelectedReleaseJson != held.SelectedReleaseSnapshot || row.ImportClaimJobId != null
            || row.ImportResultJson != held.ImportResultSnapshot || row.UpgradeOfAcquisitionId != null
            || row.Kind is not (EntityKind.VideoSeason or EntityKind.VideoEpisode)) {
            return false;
        }
        row.ImportRecoveryFingerprint = fingerprint;
        row.Status = AcquisitionStatus.Downloaded;
        row.StatusMessage = ResumeMessage;
        row.ImportClaimJobId = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        try {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        } catch (DbUpdateConcurrencyException) {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    // Match GetImportContextAsync's latest-transfer selection. A stale completed transfer cannot
    // authorize a newer incomplete payload, including when it appears after ListAsync's observation.
    private IQueryable<Persistence.Entities.AcquisitionRow> WithCompletedPayload() => db.Acquisitions
        .Where(row => db.DownloadTransfers.Where(transfer => transfer.AcquisitionId == row.Id)
            .OrderByDescending(transfer => transfer.CreatedAt)
            .Select(transfer => transfer.Progress >= 1 && !string.IsNullOrWhiteSpace(transfer.ContentPath))
            .FirstOrDefault());

    private IQueryable<Persistence.Entities.AcquisitionRow> WithObservedPayload(HeldTvImport held) => WithCompletedPayload()
        .Where(row => db.DownloadTransfers.Where(transfer => transfer.AcquisitionId == row.Id)
            .OrderByDescending(transfer => transfer.CreatedAt)
            .Select(transfer => transfer.Id == held.TransferId && transfer.ContentPath == held.TransferContentPath
                && transfer.ClientItemId == held.TransferClientItemId).FirstOrDefault());

    private static bool CanReconsiderPayload(HeldTvImport held) {
        try {
            if (held.SelectedReleaseSnapshot is null
                || JsonSerializer.Deserialize<SelectedRelease>(held.SelectedReleaseSnapshot) is not { ManualPick: false }) return false;
            if (held.CheckpointSnapshot is { } json) {
                if (AtomicUpgradeCheckpointJson.IsAtomic(json)) return false;
                var checkpoint = TvImportCheckpointJson.Deserialize(json);
                if (checkpoint is null) return false;
                using var original = JsonDocument.Parse(json);
                using var canonical = JsonDocument.Parse(TvImportCheckpointJson.Serialize(checkpoint));
                if (CheckpointJsonCompatibility.HasUnknownMembers(original.RootElement, canonical.RootElement)) return false;
                return checkpoint.TransferClientItemId == held.TransferClientItemId
                    && checkpoint.Units.All(unit => unit.PreviousFilePath is null)
                    && checkpoint.Units.Any(unit => !unit.AdoptedExistingTarget
                        && (unit.FinalPath is null || !File.Exists(unit.FinalPath)));
            }
            return held.FinalSourcePath is null
                || AcquisitionImportFileLedgerJson.TryDeserialize(held.ImportResultSnapshot, out var ledger)
                    && ledger?.HasRetainedTvVideos() == true;
        } catch (Exception ex) when (ex is InvalidDataException or JsonException) {
            return false;
        }
    }

    private const string ResumeMessage = "Import inputs changed; retrying the retained TV download with its saved recovery state.";
}
