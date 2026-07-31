using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class SubtitlesCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    private readonly Dictionary<Guid, string> _sourceKeysByTrackId = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _hydratedWritableTrackIdsByEntity = [];

    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var subtitleState = await db.EntitySubtitleStates.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EntityId == entity.Id, cancellationToken);
        var rows = await db.EntitySubtitles.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.CreatedAt)
            .ToArrayAsync(cancellationToken);

        rows = rows.Where(IsHydratable).ToArray();
        _hydratedWritableTrackIdsByEntity[entity.Id] = rows
            .Where(row => !IsPipelineManaged(row.Source))
            .Select(row => row.Id)
            .ToHashSet();
        // The lifecycle attachment and track rows are both owned by this capability.
        var capability = entity.GetCapability<CapabilitySubtitles>();
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

    public async Task ClearAsync(Entity entity, CancellationToken cancellationToken) {
        var rows = await db.EntitySubtitles
            .Where(row => row.EntityId == entity.Id &&
                row.Source != EntitySubtitleSource.Embedded &&
                row.Source != EntitySubtitleSource.Sidecar)
            .ToArrayAsync(cancellationToken);
        // Pipeline-owned embedded/sidecar rows never participate in generic Entity persistence.
        // For writable sources, clear only rows that were actually hydrated; unavailable rows remain
        // persisted so their owning workflow can recover them instead of losing intent on an edit.
        var hydratedRows = _hydratedWritableTrackIdsByEntity.Remove(entity.Id, out var hydratedIds)
            ? rows.Where(row => hydratedIds.Contains(row.Id)).ToArray()
            : rows.Where(IsHydratable).ToArray();
        foreach (var row in hydratedRows) {
            if (!string.IsNullOrWhiteSpace(row.SourceKey)) {
                _sourceKeysByTrackId[row.Id] = row.SourceKey;
            }
        }

        db.EntitySubtitles.RemoveRange(hydratedRows);
    }

    public async Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity.SubtitleCapability is not { } subtitles) {
            return;
        }

        // Extraction and sidecar reconciliation own lifecycle mutations. A generic entity save may
        // carry an older hydrated timestamp, so it may establish but never overwrite this attachment.
        if (await db.EntitySubtitleStates.FindAsync([entity.Id], cancellationToken) is null) {
            db.EntitySubtitleStates.Add(new EntitySubtitleStateRow { EntityId = entity.Id });
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var subtitle in subtitles.Items.Where(item => !IsPipelineManaged(item.Source))) {
            var subtitleId = subtitle.Id == Guid.Empty ? Guid.NewGuid() : subtitle.Id;
            var sourceKey = _sourceKeysByTrackId.Remove(subtitleId, out var preservedSourceKey)
                ? preservedSourceKey
                : SubtitleSourceKeys.Capability(subtitleId);
            db.EntitySubtitles.Add(new EntitySubtitleRow {
                Id = subtitleId,
                EntityId = entity.Id,
                Language = subtitle.Language,
                Label = subtitle.Label,
                Format = subtitle.Format,
                Source = subtitle.Source,
                SourceKey = sourceKey,
                StoragePath = subtitle.StoragePath,
                SourceFormat = subtitle.SourceFormat,
                SourcePath = subtitle.SourcePath,
                IsDefault = subtitle.IsDefault,
                CreatedAt = now,
            });
        }

    }

    private static bool IsHydratable(EntitySubtitleRow row) =>
        Path.IsPathRooted(row.StoragePath) && File.Exists(row.StoragePath);

    private static bool IsPipelineManaged(EntitySubtitleSource source) =>
        source is EntitySubtitleSource.Embedded or EntitySubtitleSource.Sidecar;
}
