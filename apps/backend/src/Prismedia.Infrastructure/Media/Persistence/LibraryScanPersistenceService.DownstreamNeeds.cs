using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>
/// Implements entity persistence operations for library scanning against the entity schema.
/// </summary>

public sealed partial class LibraryScanPersistenceService {
    public async Task<IReadOnlyDictionary<Guid, DownstreamNeeds>> CheckDownstreamNeedsBatchAsync(
        IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken) {
        if (entityIds.Count == 0) return new Dictionary<Guid, DownstreamNeeds>();

        var ids = entityIds.ToList();

        var hasTechnical = (await _db.EntityTechnical.AsNoTracking()
            .Where(t => ids.Contains(t.EntityId) && t.DurationSeconds != null)
            .Select(t => t.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var hasMediaSource = (await _db.MediaSources.AsNoTracking()
            .Where(source => ids.Contains(source.EntityId) && source.DurationSeconds != null)
            .Select(source => source.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var fingerprintRows = await _db.EntityFileFingerprints.AsNoTracking()
            .Where(f => ids.Contains(f.EntityId) &&
                (f.Algorithm == FingerprintAlgorithm.Md5 || f.Algorithm == FingerprintAlgorithm.Oshash))
            .Select(f => new { f.EntityId, f.Algorithm })
            .ToListAsync(cancellationToken);
        var hasOshash = fingerprintRows
            .Where(f => f.Algorithm == FingerprintAlgorithm.Oshash)
            .Select(f => f.EntityId)
            .ToHashSet();
        var hasMd5 = fingerprintRows
            .Where(f => f.Algorithm == FingerprintAlgorithm.Md5)
            .Select(f => f.EntityId)
            .ToHashSet();

        var entityKinds = await _db.Entities.AsNoTracking()
            .Where(entity => ids.Contains(entity.Id))
            .Select(entity => new { entity.Id, entity.KindCode })
            .ToDictionaryAsync(entity => entity.Id, entity => entity.KindCode, cancellationToken);

        var hasThumbnail = (await _db.EntityFiles.AsNoTracking()
            .Where(f => ids.Contains(f.EntityId) && f.Role == EntityFileRole.Thumbnail)
            .Select(f => f.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var hasCover = (await _db.EntityFiles.AsNoTracking()
            .Where(f => ids.Contains(f.EntityId) && (
                f.Role == EntityFileRole.Thumbnail ||
                f.Role == EntityFileRole.Poster ||
                f.Role == EntityFileRole.Cover ||
                f.Role == EntityFileRole.Logo ||
                f.Role == EntityFileRole.Backdrop))
            .Select(f => f.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var hasGridThumbnail = (await _db.EntityFiles.AsNoTracking()
            .Where(f => ids.Contains(f.EntityId) && f.Role == EntityFileRole.GridThumbnail)
            .Select(f => f.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var hasWaveform = (await _db.EntityFiles.AsNoTracking()
            .Where(f => ids.Contains(f.EntityId) && f.Role == EntityFileRole.Waveform)
            .Select(f => f.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var hasTrickplay = (await _db.TrickplayInfos.AsNoTracking()
            .Where(t => ids.Contains(t.EntityId) && t.ThumbnailCount > 0)
            .Select(t => t.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var subtitlesExtracted = (await _db.VideoDetails.AsNoTracking()
            .Where(v => ids.Contains(v.EntityId) && v.SubtitlesExtractedAt != null)
            .Select(v => v.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();
        var subtitleRows = await _db.EntitySubtitles.AsNoTracking()
            .Where(subtitle => ids.Contains(subtitle.EntityId))
            .Select(subtitle => new { subtitle.EntityId, subtitle.StoragePath })
            .ToListAsync(cancellationToken);
        var subtitlesByEntity = subtitleRows
            .GroupBy(subtitle => subtitle.EntityId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var hasUsableSubtitleState = subtitlesExtracted
            .Where(id => !subtitlesByEntity.TryGetValue(id, out var rows) ||
                rows.All(row => File.Exists(row.StoragePath)))
            .ToHashSet();

        var result = new Dictionary<Guid, DownstreamNeeds>(ids.Count);
        foreach (var id in ids) {
            var needsPreview = entityKinds.TryGetValue(id, out var kindCode) &&
                string.Equals(kindCode, EntityKindRegistry.AudioTrack.Code, StringComparison.OrdinalIgnoreCase)
                    ? !hasWaveform.Contains(id)
                    : !hasThumbnail.Contains(id);

            result[id] = new DownstreamNeeds(
                NeedsProbe: !hasTechnical.Contains(id) || !hasMediaSource.Contains(id),
                MissingOshash: !hasOshash.Contains(id),
                MissingMd5: !hasMd5.Contains(id),
                NeedsPreview: needsPreview,
                NeedsTrickplay: !hasTrickplay.Contains(id),
                NeedsSubtitleExtraction: !hasUsableSubtitleState.Contains(id),
                // Backfill: an existing cover with no small variant yet. New entities
                // (no cover at scan time) get theirs from GeneratePreview instead.
                NeedsGridThumbnail: hasCover.Contains(id) && !hasGridThumbnail.Contains(id));
        }

        return result;
    }

    // ── Reads for downstream chaining decisions ──

    public Task<bool> HasEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) =>
        _db.EntityTechnical.AnyAsync(t => t.EntityId == entityId && t.DurationSeconds != null, cancellationToken);

    public Task<bool> HasEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, CancellationToken cancellationToken) =>
        _db.EntityFileFingerprints.AnyAsync(f => f.EntityId == entityId && f.Algorithm == algorithm, cancellationToken);

    public Task<bool> HasEntityFileAsync(Guid entityId, EntityFileRole role, CancellationToken cancellationToken) =>
        _db.EntityFiles.AnyAsync(f => f.EntityId == entityId && f.Role == role, cancellationToken);

    public async Task<bool> HasSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) {
        var detail = await _db.VideoDetails.AsNoTracking()
            .FirstOrDefaultAsync(v => v.EntityId == entityId, cancellationToken);
        return detail?.SubtitlesExtractedAt is not null;
    }

}
