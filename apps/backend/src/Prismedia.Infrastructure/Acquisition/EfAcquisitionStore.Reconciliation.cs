using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
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
    /// Finds passive, non-upgrade requests whose surviving target was fulfilled by another import or scan.
    /// A target is fulfilled only when its subtree owns source media and contains no wanted Entity, so
    /// partial albums and seasons remain actionable while completed leaves and packs disappear.
    /// </summary>
    private async Task<IReadOnlySet<Guid>> ResolveFulfilledPassiveAcquisitionIdsAsync(
        IReadOnlyCollection<AcquisitionRow> rows,
        CancellationToken cancellationToken) {
        var candidates = rows
            .Where(row => row.EntityId != null
                && row.UpgradeOfAcquisitionId == null
                && OrphanRetirableStatuses.Contains(row.Status))
            .ToArray();
        if (candidates.Length == 0) {
            return new HashSet<Guid>();
        }

        var fulfilledEntityIds = await new EfEntityFulfillmentProjection(db).ResolveAsync(
            candidates.Select(row => row.EntityId!.Value).Distinct().ToArray(),
            cancellationToken);
        return candidates
            .Where(row => fulfilledEntityIds.Contains(row.EntityId!.Value))
            .Select(row => row.Id)
            .ToHashSet();
    }

    /// <summary>
    /// Removes fulfilled passive work from read projections without rewriting terminal history or
    /// deliberate upgrades. Durable monitor reconciliation remains responsible for state mutation.
    /// </summary>
    private async Task<AcquisitionRow[]> ExcludeFulfilledPassiveAcquisitionsAsync(
        AcquisitionRow[] rows,
        CancellationToken cancellationToken) {
        var fulfilledPassiveIds = await ResolveFulfilledPassiveAcquisitionIdsAsync(rows, cancellationToken);
        return rows.Where(row => !fulfilledPassiveIds.Contains(row.Id)).ToArray();
    }

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

    /// <summary>
    /// Retires passive requests in the imported Entity subtree when the completed import also fulfilled
    /// them. This covers an album pack satisfying separately requested tracks without touching partial
    /// children that remain Wanted or deliberate upgrade acquisitions.
    /// </summary>
    private async Task RetireFulfilledPassiveSubtreeAcquisitionsAsync(
        AcquisitionRow imported,
        CancellationToken cancellationToken) {
        if (imported.EntityId is not { } importedEntityId) {
            return;
        }

        var subtreeIds = await new EfEntityHierarchyReader(db).ListSubtreeIdsAsync(
            importedEntityId,
            cancellationToken);
        if (subtreeIds.Count == 0) {
            return;
        }

        var candidates = await db.Acquisitions
            .Where(candidate => candidate.Id != imported.Id
                && candidate.EntityId != null
                && subtreeIds.Contains(candidate.EntityId.Value)
                && candidate.UpgradeOfAcquisitionId == null
                && OrphanRetirableStatuses.Contains(candidate.Status))
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0) {
            return;
        }

        var fulfilledEntityIds = await new EfEntityFulfillmentProjection(db).ResolveAsync(
            candidates.Select(candidate => candidate.EntityId!.Value).Distinct().ToArray(),
            cancellationToken);
        var fulfilled = candidates
            .Where(candidate => fulfilledEntityIds.Contains(candidate.EntityId!.Value))
            .ToArray();
        if (fulfilled.Length == 0) {
            return;
        }

        var fulfilledById = fulfilled.ToDictionary(candidate => candidate.Id);
        var fulfilledIds = fulfilledById.Keys.ToHashSet();
        var now = DateTimeOffset.UtcNow;
        foreach (var candidate in fulfilled) {
            candidate.Status = AcquisitionStatus.Cancelled;
            candidate.StatusMessage = "Target Entity was fulfilled by another import or scan.";
            candidate.ImportCheckpointJson = null;
            candidate.ImportClaimJobId = null;
            candidate.UpdatedAt = now;
        }

        var monitors = await db.Monitors
            .Where(monitor => monitor.AcquisitionId != null
                && fulfilledIds.Contains(monitor.AcquisitionId.Value))
            .ToArrayAsync(cancellationToken);
        foreach (var monitor in monitors) {
            var acquisition = fulfilledById[monitor.AcquisitionId!.Value];
            monitor.EntityId ??= acquisition.EntityId;
            monitor.AcquisitionId = null;
            monitor.UpdatedAt = now;
        }

        db.AcquisitionImportHints.RemoveRange(await db.AcquisitionImportHints
            .Where(hint => fulfilledIds.Contains(hint.AcquisitionId))
            .ToArrayAsync(cancellationToken));
    }
}
