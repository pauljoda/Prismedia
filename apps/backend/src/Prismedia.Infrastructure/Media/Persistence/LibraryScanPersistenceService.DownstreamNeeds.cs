using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
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

        // Entities whose source file the probe could not read (corrupt media). No probe- or
        // ffmpeg-based work can succeed for these until the file changes on disk, which clears
        // the marker — so scans stop re-enqueueing the same doomed jobs on every pass.
        var probeFailed = (await _db.EntityTechnical.AsNoTracking()
            .Where(t => ids.Contains(t.EntityId) && t.ProbeFailedAt != null)
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

        var hasThumbnail = await LoadUsableAssetIdsAsync(ids, [EntityFileRole.Thumbnail], cancellationToken);

        var hasPreview = await LoadUsableAssetIdsAsync(ids, [EntityFileRole.Preview], cancellationToken);

        var sourcePaths = await _db.EntityFiles.AsNoTracking()
            .Where(f => ids.Contains(f.EntityId) && f.Role == EntityFileRole.Source)
            .Select(f => new { f.EntityId, f.Path })
            .ToDictionaryAsync(f => f.EntityId, f => f.Path, cancellationToken);

        var hasCover = await LoadUsableAssetIdsAsync(
            ids,
            [
                EntityFileRole.Thumbnail,
                EntityFileRole.Poster,
                EntityFileRole.Cover,
                EntityFileRole.Logo,
                EntityFileRole.Backdrop
            ],
            cancellationToken);

        var hasGridThumbnail = await LoadUsableAssetIdsAsync(ids, [EntityFileRole.GridThumbnail], cancellationToken);
        var hasGridThumbnail2x = await LoadUsableAssetIdsAsync(ids, [EntityFileRole.GridThumbnail2x], cancellationToken);

        var hasWaveform = await LoadUsableAssetIdsAsync(ids, [EntityFileRole.Waveform], cancellationToken);

        var hasTrickplay = (await _db.TrickplayInfos.AsNoTracking()
            .Where(t => ids.Contains(t.EntityId) && t.ThumbnailCount > 0)
            .Select(t => new { t.EntityId, t.Width })
            .ToListAsync(cancellationToken))
            .Where(row => HasUsableTrickplayTiles(row.EntityId, row.Width))
            .Select(row => row.EntityId)
            .ToHashSet();

        var subtitlesExtracted = (await _db.VideoDetails.AsNoTracking()
            .Where(v => ids.Contains(v.EntityId) && v.SubtitlesExtractedAt != null)
            .Select(v => v.EntityId)
            .ToListAsync(cancellationToken)).ToHashSet();
        var subtitleRows = await _db.EntitySubtitles.AsNoTracking()
            .Where(subtitle => ids.Contains(subtitle.EntityId) &&
                (subtitle.Source == EntitySubtitleSource.Embedded ||
                    subtitle.Source == EntitySubtitleSource.Sidecar))
            .Select(subtitle => new {
                subtitle.EntityId,
                subtitle.Source,
                subtitle.StoragePath,
                subtitle.SourceFormat,
                subtitle.SourcePath
            })
            .ToListAsync(cancellationToken);
        var subtitlesByEntity = subtitleRows
            .GroupBy(subtitle => subtitle.EntityId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var hasUsableSubtitleState = subtitlesExtracted
            .Where(id => !subtitlesByEntity.TryGetValue(id, out var rows) ||
                rows.All(row => SubtitleAssetAvailability.IsManagedTrackAvailable(
                    _assets,
                    row.EntityId,
                    row.Source,
                    row.StoragePath,
                    row.SourceFormat,
                    row.SourcePath)))
            .ToHashSet();

        var result = new Dictionary<Guid, DownstreamNeeds>(ids.Count);
        foreach (var id in ids) {
            entityKinds.TryGetValue(id, out var kindCode);
            var unreadable = probeFailed.Contains(id);
            var needsPreview = string.Equals(kindCode, EntityKindRegistry.AudioTrack.Code, StringComparison.OrdinalIgnoreCase)
                ? !hasWaveform.Contains(id)
                : !hasThumbnail.Contains(id) ||
                  (kindCode is not null && NeedsAnimatedImagePreviewClip(kindCode, id, sourcePaths, hasPreview));

            result[id] = new DownstreamNeeds(
                NeedsProbe: !unreadable && (!hasTechnical.Contains(id) || !hasMediaSource.Contains(id)),
                MissingOshash: !hasOshash.Contains(id),
                MissingMd5: !hasMd5.Contains(id),
                NeedsPreview: !unreadable && needsPreview,
                NeedsTrickplay: !unreadable && !hasTrickplay.Contains(id),
                // Adjacent sidecars remain importable even when the source video is unreadable.
                // Source-file changes clear both the probe marker and subtitle completion so a
                // repaired video receives a fresh embedded-stream pass as well.
                NeedsSubtitleExtraction: !hasUsableSubtitleState.Contains(id),
                // Backfill: an existing cover missing either small variant. New entities
                // (no cover at scan time) get theirs from GeneratePreview instead.
                NeedsGridThumbnail: hasCover.Contains(id) &&
                    (!hasGridThumbnail.Contains(id) || !hasGridThumbnail2x.Contains(id)));
        }

        return result;
    }

    // ── Reads for downstream chaining decisions ──

    public Task<bool> HasEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) =>
        _db.EntityTechnical.AnyAsync(t => t.EntityId == entityId && t.DurationSeconds != null, cancellationToken);

    public Task<bool> HasEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, CancellationToken cancellationToken) =>
        _db.EntityFileFingerprints.AnyAsync(f => f.EntityId == entityId && f.Algorithm == algorithm, cancellationToken);

    public Task<bool> HasEntityFileAsync(Guid entityId, EntityFileRole role, CancellationToken cancellationToken) =>
        HasUsableEntityFileAsync(entityId, role, cancellationToken);

    public Task<bool> IsEntityOrganizedAsync(Guid entityId, CancellationToken cancellationToken) =>
        _db.Entities.AsNoTracking().AnyAsync(entity => entity.Id == entityId && entity.IsOrganized, cancellationToken);

    private async Task<HashSet<Guid>> LoadUsableAssetIdsAsync(
        IReadOnlyList<Guid> ids,
        IReadOnlyCollection<EntityFileRole> roles,
        CancellationToken cancellationToken) {
        var files = await _db.EntityFiles.AsNoTracking()
            .Where(f => ids.Contains(f.EntityId) && roles.Contains(f.Role))
            .Select(f => new { f.EntityId, f.Path })
            .ToListAsync(cancellationToken);

        return files
            .Where(file => HasUsableAssetPath(file.Path))
            .Select(file => file.EntityId)
            .ToHashSet();
    }

    private async Task<bool> HasUsableEntityFileAsync(
        Guid entityId,
        EntityFileRole role,
        CancellationToken cancellationToken) {
        var paths = await _db.EntityFiles.AsNoTracking()
            .Where(f => f.EntityId == entityId && f.Role == role)
            .Select(f => f.Path)
            .ToListAsync(cancellationToken);

        return paths.Any(HasUsableAssetPath);
    }

    private bool HasUsableAssetPath(string path) {
        if (!path.StartsWith(AssetPaths.AssetsUrlPrefix, StringComparison.Ordinal)) {
            return true;
        }

        if (_assets is null) {
            return true;
        }

        var diskPath = _assets.ResolveAssetDiskPath(path);
        return diskPath is not null && File.Exists(diskPath);
    }

    private bool HasUsableTrickplayTiles(Guid entityId, int width) {
        if (_assets is null) {
            return true;
        }

        var tileDir = _assets.TrickplayTileDir(entityId, width);
        try {
            return Directory.Exists(tileDir) && Directory.EnumerateFiles(tileDir, "*.jpg").Any();
        } catch (IOException) {
            return false;
        } catch (UnauthorizedAccessException) {
            return false;
        }
    }

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

    public async Task<IReadOnlyList<EntityRefreshTarget>> GetAudioContainerTargetsInRootAsync(
        Guid rootId, CancellationToken cancellationToken) {
        var albumIds = await _db.AudioLibraryDetails.AsNoTracking()
            .Where(detail => detail.LibraryRootId == rootId)
            .Select(detail => detail.EntityId)
            .ToListAsync(cancellationToken);
        var artistIds = await _db.MusicArtistDetails.AsNoTracking()
            .Where(detail => detail.LibraryRootId == rootId)
            .Select(detail => detail.EntityId)
            .ToListAsync(cancellationToken);

        var containerIds = albumIds.Concat(artistIds).Distinct().ToArray();
        if (containerIds.Length == 0) {
            return [];
        }

        return await _db.Entities.AsNoTracking()
            .Where(entity => containerIds.Contains(entity.Id))
            .OrderBy(entity => entity.Title)
            .Select(entity => new EntityRefreshTarget(entity.Id, entity.KindCode, entity.Title))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EntityRefreshTarget>> GetAudioTrackTargetsInRootAsync(
        Guid rootId, CancellationToken cancellationToken) {
        var albumIds = await _db.AudioLibraryDetails.AsNoTracking()
            .Where(detail => detail.LibraryRootId == rootId)
            .Select(detail => detail.EntityId)
            .ToListAsync(cancellationToken);

        var targets = new Dictionary<Guid, EntityRefreshTarget>();
        if (albumIds.Count > 0) {
            var albumTracks = await _db.Entities.AsNoTracking()
                .Where(entity => entity.KindCode == EntityKindRegistry.AudioTrack.Code &&
                    entity.ParentEntityId != null &&
                    albumIds.Contains(entity.ParentEntityId.Value))
                .OrderBy(entity => entity.ParentEntityId)
                .ThenBy(entity => entity.SortOrder)
                .ThenBy(entity => entity.Title)
                .Select(entity => new EntityRefreshTarget(entity.Id, entity.KindCode, entity.Title))
                .ToListAsync(cancellationToken);

            foreach (var track in albumTracks) {
                targets[track.Id] = track;
            }
        }

        var rootPath = await _db.LibraryRoots.AsNoTracking()
            .Where(root => root.Id == rootId)
            .Select(root => root.Path)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(rootPath)) {
            var looseTracks = await _db.EntityFiles.AsNoTracking()
                .Where(file => file.Role == EntityFileRole.Source)
                .Join(
                    _db.Entities.AsNoTracking().Where(entity =>
                        entity.KindCode == EntityKindRegistry.AudioTrack.Code &&
                        entity.ParentEntityId == null),
                    file => file.EntityId,
                    entity => entity.Id,
                    (file, entity) => new { entity.Id, entity.KindCode, entity.Title, file.Path })
                .ToListAsync(cancellationToken);

            foreach (var track in looseTracks.Where(track =>
                         LibraryScanPathRules.IsDirectChildPath(track.Path, rootPath))) {
                targets[track.Id] = new EntityRefreshTarget(track.Id, track.KindCode, track.Title);
            }
        }

        return targets.Values.ToList();
    }

    public async Task<IReadOnlyList<AutoIdentifyRootTarget>> ResolveAutoIdentifyRootsForLibraryRootAsync(
        Guid libraryRootId,
        IReadOnlyList<MediaCategory> scanCategories,
        CancellationToken cancellationToken) {
        if (scanCategories.Count == 0) return [];

        var categories = scanCategories.ToHashSet();
        var entityIds = new List<Guid>();

        if (categories.Contains(MediaCategory.Video)) {
            var videoIds = await _db.VideoDetails.AsNoTracking()
                .Where(detail => detail.LibraryRootId == libraryRootId)
                .Select(detail => detail.EntityId)
                .ToListAsync(cancellationToken);
            entityIds.AddRange(videoIds);
        }

        if (categories.Contains(MediaCategory.Image)) {
            var galleryIds = await _db.GalleryDetails.AsNoTracking()
                .Where(detail => detail.LibraryRootId == libraryRootId)
                .Select(detail => detail.EntityId)
                .ToListAsync(cancellationToken);
            entityIds.AddRange(galleryIds);
        }

        if (categories.Contains(MediaCategory.Audio)) {
            // Preserve the full-scan queue ordering: artists first, then albums. When an artist
            // identifies successfully, later album jobs can use the saved artist external IDs as
            // provider context.
            var artistIds = await _db.MusicArtistDetails.AsNoTracking()
                .Where(detail => detail.LibraryRootId == libraryRootId)
                .Select(detail => detail.EntityId)
                .ToListAsync(cancellationToken);
            entityIds.AddRange(artistIds);

            var albumIds = await _db.AudioLibraryDetails.AsNoTracking()
                .Where(detail => detail.LibraryRootId == libraryRootId)
                .Select(detail => detail.EntityId)
                .ToListAsync(cancellationToken);
            entityIds.AddRange(albumIds);
        }

        if (categories.Contains(MediaCategory.ComicArchive) || categories.Contains(MediaCategory.Book)) {
            var bookIds = await _db.BookDetails.AsNoTracking()
                .Where(detail => detail.LibraryRootId == libraryRootId)
                .Select(detail => detail.EntityId)
                .ToListAsync(cancellationToken);
            entityIds.AddRange(bookIds);
        }

        return await ResolveAutoIdentifyRootsAsync(entityIds.Distinct().ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<AutoIdentifyRootTarget>> ResolveAutoIdentifyRootsAsync(
        IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken) {
        if (entityIds.Count == 0) return [];

        // Load the scanned entities plus their ancestor chain in waves (libraries are only a few
        // levels deep), then walk each scanned entity up to its top-level ancestor.
        var info = new Dictionary<Guid, (string KindCode, string Title, Guid? ParentId, int AutoIdentifyAttempts, bool IsOrganized)>();
        var toLoad = new HashSet<Guid>(entityIds);
        while (toLoad.Count > 0) {
            var batch = toLoad.ToList();
            toLoad.Clear();
            var rows = await _db.Entities.AsNoTracking()
                .Where(entity => batch.Contains(entity.Id))
                .Select(entity => new { entity.Id, entity.KindCode, entity.Title, entity.ParentEntityId, entity.AutoIdentifyAttempts, entity.IsOrganized })
                .ToListAsync(cancellationToken);
            foreach (var row in rows) {
                info[row.Id] = (row.KindCode, row.Title, row.ParentEntityId, row.AutoIdentifyAttempts, row.IsOrganized);
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
                roots[current] = new AutoIdentifyRootTarget(current, rootInfo.KindCode, rootInfo.Title, rootInfo.IsOrganized);
            }
        }

        return roots.Values.ToList();
    }

}
