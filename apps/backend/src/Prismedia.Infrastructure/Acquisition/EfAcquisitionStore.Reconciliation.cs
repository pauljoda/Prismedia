using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Provider-identity and removed-target reconciliation for <see cref="EfAcquisitionStore"/>.</summary>
public sealed partial class EfAcquisitionStore {
    private static readonly AcquisitionStatus[] OrphanRetirableStatuses = [
        AcquisitionStatus.Pending,
        AcquisitionStatus.Searching,
        AcquisitionStatus.AwaitingSelection,
        AcquisitionStatus.Failed,
        AcquisitionStatus.ManualImportRequired
    ];

    /// <summary>
    /// Retires older passive requests for the same provider item after a successful import becomes
    /// authoritative. Transfer-owning duplicates remain visible for their normal teardown workflow.
    /// </summary>
    private async Task RetireSupersededPassiveDuplicatesAsync(
        AcquisitionRow imported,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(imported.IdentityNamespace)
            || string.IsNullOrWhiteSpace(imported.IdentityValue)) {
            return;
        }

        var superseded = await db.Acquisitions
            .Where(candidate => candidate.Id != imported.Id
                && candidate.Kind == imported.Kind
                && candidate.BookRendition == imported.BookRendition
                && candidate.SeasonNumber == imported.SeasonNumber
                && candidate.EpisodeNumber == imported.EpisodeNumber
                && candidate.VolumeNumber == imported.VolumeNumber
                && candidate.IdentityNamespace == imported.IdentityNamespace
                && candidate.IdentityValue == imported.IdentityValue
                && OrphanRetirableStatuses.Contains(candidate.Status))
            .ToArrayAsync(cancellationToken);
        if (superseded.Length == 0) {
            return;
        }

        var supersededIds = superseded.Select(candidate => candidate.Id).ToArray();
        var now = DateTimeOffset.UtcNow;
        foreach (var candidate in superseded) {
            candidate.Status = AcquisitionStatus.Cancelled;
            candidate.StatusMessage = "Superseded by an imported acquisition for the same item.";
            candidate.ImportCheckpointJson = null;
            candidate.ImportClaimJobId = null;
            candidate.UpdatedAt = now;
        }

        db.Monitors.RemoveRange(await db.Monitors
            .Where(monitor => monitor.AcquisitionId != null
                && supersededIds.Contains(monitor.AcquisitionId.Value))
            .ToArrayAsync(cancellationToken));
        db.AcquisitionImportHints.RemoveRange(await db.AcquisitionImportHints
            .Where(hint => supersededIds.Contains(hint.AcquisitionId))
            .ToArrayAsync(cancellationToken));
    }
}
