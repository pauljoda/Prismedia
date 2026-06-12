using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
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

        var hasPreview = (await _db.EntityFiles.AsNoTracking()
            .Where(f => ids.Contains(f.EntityId) && f.Role == EntityFileRole.Preview)
            .Select(f => f.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var sourcePaths = await _db.EntityFiles.AsNoTracking()
            .Where(f => ids.Contains(f.EntityId) && f.Role == EntityFileRole.Source)
            .Select(f => new { f.EntityId, f.Path })
            .ToDictionaryAsync(f => f.EntityId, f => f.Path, cancellationToken);

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
            entityKinds.TryGetValue(id, out var kindCode);
            var needsPreview = string.Equals(kindCode, EntityKindRegistry.AudioTrack.Code, StringComparison.OrdinalIgnoreCase)
                ? !hasWaveform.Contains(id)
                : !hasThumbnail.Contains(id) ||
                  (kindCode is not null && NeedsAnimatedImagePreviewClip(kindCode, id, sourcePaths, hasPreview));

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

    private static bool NeedsAnimatedImagePreviewClip(
        string kindCode,
        Guid entityId,
        IReadOnlyDictionary<Guid, string> sourcePaths,
        IReadOnlySet<Guid> hasPreview) =>
        string.Equals(kindCode, EntityKindRegistry.Image.Code, StringComparison.OrdinalIgnoreCase) &&
        sourcePaths.TryGetValue(entityId, out var sourcePath) &&
        AnimatedImagePreviewPolicy.RequiresPreviewClip(sourcePath) &&
        !hasPreview.Contains(entityId);

    public async Task<bool> HasSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) {
        var detail = await _db.VideoDetails.AsNoTracking()
            .FirstOrDefaultAsync(v => v.EntityId == entityId, cancellationToken);
        return detail?.SubtitlesExtractedAt is not null;
    }

    public async Task<IReadOnlyList<AutoIdentifyRootTarget>> ResolveAutoIdentifyRootsAsync(
        IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken) {
        if (entityIds.Count == 0) return [];

        // Load the scanned entities plus their ancestor chain in waves (libraries are only a few
        // levels deep), then walk each scanned entity up to its top-level ancestor.
        var info = new Dictionary<Guid, (string KindCode, string Title, Guid? ParentId, int AutoIdentifyAttempts)>();
        var toLoad = new HashSet<Guid>(entityIds);
        while (toLoad.Count > 0) {
            var batch = toLoad.ToList();
            toLoad.Clear();
            var rows = await _db.Entities.AsNoTracking()
                .Where(entity => batch.Contains(entity.Id))
                .Select(entity => new { entity.Id, entity.KindCode, entity.Title, entity.ParentEntityId, entity.AutoIdentifyAttempts })
                .ToListAsync(cancellationToken);
            foreach (var row in rows) {
                info[row.Id] = (row.KindCode, row.Title, row.ParentEntityId, row.AutoIdentifyAttempts);
                if (row.ParentEntityId is { } parentId && !info.ContainsKey(parentId)) {
                    toLoad.Add(parentId);
                }
            }
        }

        var musicArtistCode = EntityKindRegistry.MusicArtist.Code;
        var roots = new Dictionary<Guid, AutoIdentifyRootTarget>();
        foreach (var id in entityIds) {
            var current = id;
            var guard = 0;
            while (info.TryGetValue(current, out var node) && node.ParentId is { } parent && guard++ < 64) {
                // An artist grouping is identified on demand, not as part of the scan cascade, so an
                // album stays its own auto-identify root rather than collapsing into the artist.
                if (info.TryGetValue(parent, out var parentNode) && parentNode.KindCode == musicArtistCode) {
                    break;
                }

                current = parent;
            }

            if (!roots.ContainsKey(current) && info.TryGetValue(current, out var rootInfo) &&
                rootInfo.AutoIdentifyAttempts < AutoIdentifyPolicy.MaxAttemptsPerEntity) {
                roots[current] = new AutoIdentifyRootTarget(current, rootInfo.KindCode, rootInfo.Title);
            }
        }

        return roots.Values.ToList();
    }

}
