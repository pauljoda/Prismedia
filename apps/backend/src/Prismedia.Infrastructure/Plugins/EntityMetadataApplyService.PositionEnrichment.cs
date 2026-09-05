using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class EntityMetadataApplyService {
    /// <inheritdoc />
    public async Task<EntityMetadataPatchResult> FillMissingPositionsAsync(
        Guid entityId,
        EntityKind expectedKind,
        IReadOnlyDictionary<string, int> positions,
        CancellationToken cancellationToken) {
        if (!await CanEditCollectionAsync(entityId, cancellationToken)) {
            return EntityMetadataPatchResult.NotFound;
        }

        var result = EntityMetadataPatchResult.NotFound;
        var accepted = await _lifecycle.ExecuteAsync(entityId, async leaseCancellationToken => {
            var entity = await _db.Entities.FirstOrDefaultAsync(row => row.Id == entityId, leaseCancellationToken);
            if (entity is null) {
                return;
            }
            if (entity.KindCode != expectedKind.ToCode()) {
                result = EntityMetadataPatchResult.KindMismatch;
                return;
            }

            var current = await _db.EntityPositions
                .Where(row => row.EntityId == entityId)
                .ToDictionaryAsync(row => row.Code, row => row.Value, leaseCancellationToken);
            var merged = EntityMetadataPositionRules.Normalize(positions).ToDictionary();
            foreach (var (code, value) in current) {
                merged[code] = value;
            }
            // Use the complete, gap-filled ordering to repair the derived sort order as well. A partial
            // provider patch must never remove absolute ordering or overwrite an existing correction.
            await UpsertPositionsAsync(entity, merged, DateTimeOffset.UtcNow, leaseCancellationToken);
            await _db.SaveChangesAsync(leaseCancellationToken);
            result = EntityMetadataPatchResult.Applied;
        }, cancellationToken);
        return accepted ? result : EntityMetadataPatchResult.NotFound;
    }
}
