using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

/// <summary>Hydrates the generic page-sequence summary from a persisted, source-backed manifest.</summary>
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

        var ordinals = await db.EntityPageEntries.AsNoTracking()
            .Where(row => row.EntityId == entity.Id)
            .OrderBy(row => row.Ordinal)
            .Select(row => row.Ordinal)
            .ToArrayAsync(cancellationToken);
        if (ordinals.Length == 0 ||
            ordinals.Where((ordinal, index) => ordinal != index).Any() ||
            header.CoverOrdinal is < 0 || header.CoverOrdinal >= ordinals.Length) {
            return;
        }

        entity.RemoveCapability<CapabilityPageSequence>();
        entity.AddCapability(new CapabilityPageSequence(
            ordinals.Length,
            header.Direction,
            header.DefaultMode,
            header.CoverOrdinal));
    }
}
