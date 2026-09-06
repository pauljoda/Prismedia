using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

internal static class OwnedVideoEvidence {
    /// <summary>Shared physical video sources require a coverage-aware merge, even when an older receipt describes one episode.</summary>
    internal static Task<bool> IsSharedAsync(PrismediaDbContext db, Guid entityId, CancellationToken cancellationToken) =>
        db.EntityFiles.AsNoTracking().AnyAsync(source => source.EntityId == entityId && source.Role == EntityFileRole.Source
            && db.EntityFiles.Any(other => other.EntityId != entityId && other.Role == EntityFileRole.Source
                && other.Path == source.Path), cancellationToken);

    /// <summary>Reads dimensions only from the current single source's matching probe; detached, failed, or size-stale metadata is not upgrade evidence.</summary>
    internal static async Task<int?> ReadResolutionAsync(PrismediaDbContext db, Guid entityId, CancellationToken cancellationToken) {
        var sources = await db.EntityFiles.AsNoTracking()
            .Where(file => file.EntityId == entityId && file.Role == EntityFileRole.Source)
            .Select(file => new { file.Id, file.Path, file.SizeBytes }).Take(2).ToArrayAsync(cancellationToken);
        if (sources.Length != 1 || sources[0].SizeBytes is not > 0
            || await db.EntityTechnical.AsNoTracking().AnyAsync(row => row.EntityId == entityId && row.ProbeFailedAt != null, cancellationToken)) return null;
        var source = sources[0];
        var measured = await db.MediaSources.AsNoTracking()
            .Where(media => media.EntityId == entityId && media.EntityFileId == source.Id
                && media.Path == source.Path && media.SizeBytes == source.SizeBytes)
            .Select(media => new { media.Width, media.Height }).Take(2).ToArrayAsync(cancellationToken);
        return measured.Length == 1 ? VideoPayloadProfileValidation.ResolutionTier(measured[0].Width, measured[0].Height) : null;
    }
}
