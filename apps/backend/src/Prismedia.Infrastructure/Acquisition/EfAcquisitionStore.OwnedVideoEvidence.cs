using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfAcquisitionStore {
    /// <summary>Reads dimensions only from the current single source's matching probe; detached, failed, or size-stale metadata is not upgrade evidence.</summary>
    private async Task<int?> GetOwnedVideoResolutionAsync(Guid entityId, CancellationToken cancellationToken) {
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
