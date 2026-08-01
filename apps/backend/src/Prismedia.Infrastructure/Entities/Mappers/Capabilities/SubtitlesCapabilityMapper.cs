using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class SubtitlesCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var subtitleState = await db.EntitySubtitleStates.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EntityId == entity.Id, cancellationToken);
        var rows = await db.EntitySubtitles.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.CreatedAt)
            .ToArrayAsync(cancellationToken);

        rows = rows.Where(IsHydratable).ToArray();
        // The lifecycle attachment and track rows are both owned by this capability.
        var capability = entity.GetCapability<CapabilitySubtitles>();
        if (capability is null && subtitleState is null && rows.Length == 0) {
            return;
        }
        if (capability is null) {
            capability = new CapabilitySubtitles();
            entity.AddCapability(capability);
        }

        capability.MarkExtracted(subtitleState?.SubtitlesExtractedAt);
        if (rows.Length == 0) {
            return;
        }

        capability.Hydrate(rows.Select(r => new CapabilitySubtitles.Item(
            r.Id, r.Language, r.Label, r.Format, r.Source,
            r.StoragePath, r.SourceFormat, r.SourcePath, r.IsDefault)).ToArray());
    }

    private static bool IsHydratable(EntitySubtitleRow row) =>
        Path.IsPathRooted(row.StoragePath) && File.Exists(row.StoragePath);
}
