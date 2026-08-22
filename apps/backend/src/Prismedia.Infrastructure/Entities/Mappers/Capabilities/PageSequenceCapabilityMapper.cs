using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

/// <summary>
/// Hydrates the generic page-sequence summary from a persisted, source-backed manifest and its
/// scan-maintained page-count statistic. Full manifest entries are loaded only by the reader.
/// </summary>
internal sealed class PageSequenceCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    /// <inheritdoc />
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var header = await db.EntityPageManifests.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EntityId == entity.Id, cancellationToken);
        if (header is null || !await db.EntityFiles.AsNoTracking().AnyAsync(
                row => row.EntityId == entity.Id && row.Role == EntityFileRole.Source,
                cancellationToken)) {
            return;
        }

        var pageCount = await db.EntityStats.AsNoTracking()
            .Where(row => row.EntityId == entity.Id && row.Code == EntityStatCodes.Pages)
            .Select(row => (int?)row.Value)
            .SingleOrDefaultAsync(cancellationToken);
        if (pageCount is null or <= 0 ||
            header.CoverOrdinal is < 0 || header.CoverOrdinal >= pageCount) {
            return;
        }

        entity.RemoveCapability<CapabilityPageSequence>();
        entity.AddCapability(new CapabilityPageSequence(
            pageCount.Value,
            header.Direction,
            header.DefaultMode,
            header.CoverOrdinal));
    }
}
