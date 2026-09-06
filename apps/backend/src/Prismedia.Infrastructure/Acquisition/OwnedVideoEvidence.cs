using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

internal static class OwnedVideoEvidence {
    /// <summary>
    /// Composable current-source evidence for monitor projections, avoiding a database round trip per owned item.
    /// The caller owns tracking: a no-tracking annotation here would also disable the sweep's monitor mutations.
    /// </summary>
    internal static IQueryable<MediaSourceRow> CurrentSources(PrismediaDbContext db) =>
        from media in db.MediaSources
        join source in db.EntityFiles on media.EntityFileId equals source.Id
        where source.Role == EntityFileRole.Source && source.EntityId == media.EntityId
            && source.SizeBytes > 0 && source.SizeBytes == media.SizeBytes && source.Path == media.Path
            && !db.EntityTechnical.Any(row => row.EntityId == media.EntityId && row.ProbeFailedAt != null)
            && db.EntityFiles.Count(row => row.EntityId == media.EntityId && row.Role == EntityFileRole.Source) == 1
            && db.MediaSources.Count(row => row.EntityId == media.EntityId && row.EntityFileId == source.Id
                && row.Path == source.Path && row.SizeBytes == source.SizeBytes) == 1
        select media;

    /// <summary>Shared physical video sources require a coverage-aware merge, even when an older receipt describes one episode.</summary>
    internal static Task<bool> IsSharedAsync(PrismediaDbContext db, Guid entityId, CancellationToken cancellationToken) =>
        db.EntityFiles.AsNoTracking().AnyAsync(source => source.EntityId == entityId && source.Role == EntityFileRole.Source
            && db.EntityFiles.Any(other => other.EntityId != entityId && other.Role == EntityFileRole.Source
                && other.Path == source.Path), cancellationToken);

    /// <summary>Reads dimensions only from the current single source's matching probe; detached, failed, or size-stale metadata is not upgrade evidence.</summary>
    internal static async Task<int?> ReadResolutionAsync(PrismediaDbContext db, Guid entityId, CancellationToken cancellationToken) {
        var measured = await CurrentSources(db).AsNoTracking().Where(media => media.EntityId == entityId)
            .Select(media => new { media.Width, media.Height }).Take(2).ToArrayAsync(cancellationToken);
        return measured.Length == 1 ? VideoPayloadProfileValidation.ResolutionTier(measured[0].Width, measured[0].Height) : null;
    }
}
