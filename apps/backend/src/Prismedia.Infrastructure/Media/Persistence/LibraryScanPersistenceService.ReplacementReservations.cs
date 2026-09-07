using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Media.Persistence;

public sealed partial class LibraryScanPersistenceService {
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListPendingVideoReplacementPathsAsync(CancellationToken cancellationToken) {
        var paths = new HashSet<string>(FileSystemPathComparison.Comparer);
        foreach (var pending in await ListPendingVideoReplacementsAsync(cancellationToken)) {
            // Even malformed evidence must not allow stale cleanup to erase the retained owner.
            if (!string.IsNullOrWhiteSpace(pending.OwnedPath)) paths.Add(pending.OwnedPath);
            try {
                var checkpoint = AtomicUpgradeCheckpointJson.Deserialize(pending.CheckpointJson);
                if (checkpoint.ParentAcquisitionId != pending.ParentId || checkpoint.ParentEntityId != pending.EntityId
                    || checkpoint.Kind != pending.Kind) continue;
                paths.Add(checkpoint.Files.OwnedPath);
                paths.Add(checkpoint.Files.InstallPath);
            } catch (InvalidDataException) {
                // Recovery surfaces the damaged checkpoint for review; scanning cannot repair it.
            }
        }
        return paths.ToArray();
    }

    private async Task<List<PendingVideoReplacement>> ListPendingVideoReplacementsAsync(CancellationToken cancellationToken) {
        var kinds = EntityKindRegistry.All.OfType<IPlayableVideoKindDefinition>().Select(definition => definition.Kind).ToArray();
        return await _db.Acquisitions.AsNoTracking()
            .Where(child => child.UpgradeOfAcquisitionId != null && child.ImportCheckpointJson != null && kinds.Contains(child.Kind))
            .Join(_db.Acquisitions.AsNoTracking().Where(parent => parent.EntityId != null),
                child => child.UpgradeOfAcquisitionId, parent => (Guid?)parent.Id,
                (child, parent) => new PendingVideoReplacement(parent.Id, parent.EntityId!.Value, child.Kind,
                    parent.FinalSourcePath, child.ImportCheckpointJson!))
            .ToListAsync(cancellationToken);
    }

    private async Task<HashSet<Guid>> GetPendingReplacementProtectedEntitiesAsync(CancellationToken cancellationToken) {
        var protectedIds = (await ListPendingVideoReplacementsAsync(cancellationToken)).Select(pending => pending.EntityId).ToHashSet();
        var frontier = protectedIds.ToArray();
        // Preserve ancestors too: deleting a stale container can cascade into a retained episode.
        while (frontier.Length > 0) {
            var parents = await _db.Entities.AsNoTracking()
                .Where(entity => frontier.Contains(entity.Id) && entity.ParentEntityId != null)
                .Select(entity => entity.ParentEntityId!.Value).Distinct().ToListAsync(cancellationToken);
            frontier = parents.Where(protectedIds.Add).ToArray();
        }
        return protectedIds;
    }

    private sealed record PendingVideoReplacement(Guid ParentId, Guid EntityId, EntityKind Kind, string? OwnedPath, string CheckpointJson);
}
