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
                && row.ImportCheckpointJson == null
                && row.UpgradeOfAcquisitionId == null)
            .OrderBy(row => row.UpdatedAt)
            .Select(row => new HeldTvImport(row.Id, row.EntityId!.Value, row.UpdatedAt, row.ImportRecoveryFingerprint,
                row.FinalSourcePath, row.ImportResultJson))
            .ToArrayAsync(cancellationToken);
        return held.Where(CanReconsiderPayload).ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> TryResumeAsync(HeldTvImport held, string fingerprint, CancellationToken cancellationToken) {
        if (!CanReconsiderPayload(held)) return false;
        if (db.Database.IsRelational()) {
            var affected = await WithCompletedPayload()
                .Where(row => row.Id == held.Id && row.Status == AcquisitionStatus.ManualImportRequired
                    && row.UpdatedAt == held.HeldAt && row.EntityId == held.EntityId
                    && row.ImportRecoveryFingerprint != fingerprint && !row.ImportManualReview
                    && row.ImportCheckpointJson == null && row.FinalSourcePath == held.FinalSourcePath
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
        var row = await WithCompletedPayload().FirstOrDefaultAsync(row => row.Id == held.Id, cancellationToken);
        if (row is null || row.Status != AcquisitionStatus.ManualImportRequired || row.UpdatedAt != held.HeldAt
            || row.EntityId != held.EntityId || row.ImportRecoveryFingerprint == fingerprint || row.ImportManualReview
            || row.ImportCheckpointJson != null || row.FinalSourcePath != held.FinalSourcePath
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

    private static bool CanReconsiderPayload(HeldTvImport held) => held.FinalSourcePath is null
        || AcquisitionImportFileLedgerJson.TryDeserialize(held.ImportResultSnapshot, out var ledger)
            && ledger?.HasRetainedTvVideos() == true;

    private const string ResumeMessage = "Mapping inputs changed; retrying the retained TV download to fill library gaps.";
}
