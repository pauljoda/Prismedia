using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Persists retained TV retry observations alongside the acquisition's durable completion state.</summary>
public sealed class EfHeldTvImportRecoveryStore(PrismediaDbContext db) : IHeldTvImportRecoveryStore {
    /// <inheritdoc />
    public async Task<IReadOnlyList<HeldTvImport>> ListAsync(CancellationToken cancellationToken) {
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
