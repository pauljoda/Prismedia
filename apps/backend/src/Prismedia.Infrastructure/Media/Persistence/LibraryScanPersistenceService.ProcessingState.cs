using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Media.Persistence;

/// <summary>
/// Implements entity persistence operations for library scanning against the entity schema.
/// </summary>

public sealed partial class LibraryScanPersistenceService {
    // ── Entity technical / file / fingerprint writes ──

    public async Task UpsertEntityTechnicalAsync(Guid entityId, double? duration, int? width, int? height,
        double? frameRate, int? bitRate, int? sampleRate, int? channels,
        string? codec, string? container, string? format, CancellationToken cancellationToken) {
        var existing = await _db.EntityTechnical.FindAsync([entityId], cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is not null) {
            existing.DurationSeconds = duration ?? existing.DurationSeconds;
            existing.Width = width ?? existing.Width;
            existing.Height = height ?? existing.Height;
            existing.FrameRate = frameRate ?? existing.FrameRate;
            existing.BitRate = bitRate ?? existing.BitRate;
            existing.SampleRate = sampleRate ?? existing.SampleRate;
            existing.Channels = channels ?? existing.Channels;
            existing.Codec = codec ?? existing.Codec;
            existing.Container = container ?? existing.Container;
            existing.Format = format ?? existing.Format;
            // A successful probe supersedes any earlier unreadable-source marker.
            existing.ProbeFailedAt = null;
            existing.UpdatedAt = now;
        } else {
            _db.EntityTechnical.Add(new EntityTechnicalRow {
                EntityId = entityId,
                DurationSeconds = duration,
                Width = width,
                Height = height,
                FrameRate = frameRate,
                BitRate = bitRate,
                SampleRate = sampleRate,
                Channels = channels,
                Codec = codec,
                Container = container,
                Format = format,
                UpdatedAt = now
            });
        }

        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task UpsertMediaSourceAsync(
        Guid entityId,
        string path,
        MediaSourceProbeData source,
        IReadOnlyList<MediaStreamProbeData> streams,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var sourceFileId = await GetSourceFileIdAsync(entityId, cancellationToken);
        var existing = await _db.MediaSources
            .FirstOrDefaultAsync(row => row.EntityId == entityId && row.Path == path, cancellationToken);

        if (existing is null) {
            existing = new MediaSourceRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Path = path,
                Protocol = "File",
                CreatedAt = now
            };
            _db.MediaSources.Add(existing);
        }

        existing.EntityFileId = sourceFileId;
        existing.Container = source.Container ?? existing.Container;
        existing.Name = Path.GetFileName(path);
        existing.SizeBytes = source.SizeBytes ?? existing.SizeBytes ?? LibraryScanFileSystem.TryGetFileSize(path);
        existing.DurationSeconds = source.DurationSeconds ?? existing.DurationSeconds;
        existing.BitRate = source.BitRate ?? existing.BitRate;
        existing.VideoCodec = source.VideoCodec ?? existing.VideoCodec;
        existing.AudioCodec = source.AudioCodec ?? existing.AudioCodec;
        existing.Width = source.Width ?? existing.Width;
        existing.Height = source.Height ?? existing.Height;
        existing.FrameRate = source.FrameRate ?? existing.FrameRate;
        existing.UpdatedAt = now;

        var previousStreams = await _db.MediaStreams
            .Where(row => row.MediaSourceId == existing.Id)
            .ToListAsync(cancellationToken);
        _db.MediaStreams.RemoveRange(previousStreams);

        foreach (var stream in streams) {
            _db.MediaStreams.Add(new MediaStreamRow {
                Id = Guid.NewGuid(),
                MediaSourceId = existing.Id,
                EntityId = entityId,
                StreamIndex = stream.StreamIndex,
                Type = stream.Type,
                Codec = stream.Codec,
                Language = stream.Language,
                Title = stream.Title,
                Width = stream.Width,
                Height = stream.Height,
                FrameRate = stream.FrameRate,
                BitRate = stream.BitRate,
                SampleRate = stream.SampleRate,
                Channels = stream.Channels,
                PixelFormat = stream.PixelFormat,
                BitDepth = stream.BitDepth,
                ColorRange = stream.ColorRange,
                ColorSpace = stream.ColorSpace,
                ColorTransfer = stream.ColorTransfer,
                ColorPrimaries = stream.ColorPrimaries,
                DvProfile = stream.DvProfile,
                DvLevel = stream.DvLevel,
                RpuPresentFlag = stream.RpuPresentFlag,
                ElPresentFlag = stream.ElPresentFlag,
                BlPresentFlag = stream.BlPresentFlag,
                DvBlSignalCompatibilityId = stream.DvBlSignalCompatibilityId,
                Hdr10PlusPresentFlag = stream.Hdr10PlusPresentFlag,
                IsDefault = stream.IsDefault,
                IsForced = stream.IsForced,
                CreatedAt = now
            });
        }

        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task UpsertTrickplayInfoAsync(
        Guid entityId,
        TrickplayInfoData info,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var existing = await _db.TrickplayInfos.FindAsync([entityId, info.Width], cancellationToken);
        if (existing is null) {
            existing = new TrickplayInfoRow {
                EntityId = entityId,
                Width = info.Width,
                CreatedAt = now
            };
            _db.TrickplayInfos.Add(existing);
        }

        existing.Height = info.Height;
        existing.TileWidth = info.TileWidth;
        existing.TileHeight = info.TileHeight;
        existing.ThumbnailCount = info.ThumbnailCount;
        existing.IntervalSeconds = info.IntervalSeconds;
        existing.Bandwidth = info.Bandwidth;
        existing.UpdatedAt = now;
        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task UpsertEntityFileAsync(Guid entityId, EntityFileRole role, string path, string? mimeType, long? sizeBytes, CancellationToken cancellationToken) {
        var existing = await _db.EntityFiles
            .FirstOrDefaultAsync(f => f.EntityId == entityId && f.Role == role, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is not null) {
            // Scan-generated writes never overwrite user-uploaded custom assets.
            if (existing.Source == FileSourceKind.Custom.ToCode())
                return;

            existing.Path = path;
            existing.MimeType = mimeType ?? existing.MimeType;
            existing.SizeBytes = sizeBytes ?? existing.SizeBytes;
            existing.UpdatedAt = now;
        } else {
            _db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Role = role,
                Path = path,
                MimeType = mimeType,
                SizeBytes = sizeBytes,
                Source = FileSourceKind.Scan.ToCode(),
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task UpsertEntityFingerprintAsync(Guid entityId, FingerprintAlgorithm algorithm, string value, Guid? entityFileId, CancellationToken cancellationToken) {
        var existing = await _db.EntityFileFingerprints
            .FirstOrDefaultAsync(f => f.EntityId == entityId && f.Algorithm == algorithm, cancellationToken);

        if (existing is not null) {
            existing.Value = value;
            existing.EntityFileId = entityFileId;
        } else {
            _db.EntityFileFingerprints.Add(new EntityFileFingerprintRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                EntityFileId = entityFileId,
                Algorithm = algorithm,
                Value = value,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task<Guid?> GetSourceFileIdAsync(Guid entityId, CancellationToken cancellationToken) {
        return await _db.EntityFiles.AsNoTracking()
            .Where(f => f.EntityId == entityId && f.Role == EntityFileRole.Source)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetSourceFilePathAsync(Guid entityId, CancellationToken cancellationToken) {
        return await _db.EntityFiles.AsNoTracking()
            .Where(f => f.EntityId == entityId && f.Role == EntityFileRole.Source)
            .Select(f => f.Path)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task ReplaceEmbeddedAudioChaptersAsync(
        Guid entityId,
        IReadOnlyList<AudioChapterProbeData> chapters,
        CancellationToken cancellationToken) {
        var existing = await _db.EntityMarkers
            .Where(row => row.EntityId == entityId && row.SourceIndex != null)
            .ToArrayAsync(cancellationToken);
        var ordered = chapters
            .Where(chapter => double.IsFinite(chapter.StartSeconds) &&
                double.IsFinite(chapter.EndSeconds) &&
                chapter.StartSeconds >= 0 &&
                chapter.EndSeconds > chapter.StartSeconds)
            .OrderBy(chapter => chapter.Index)
            .ThenBy(chapter => chapter.StartSeconds)
            .GroupBy(chapter => chapter.Index)
            .Select(group => group.First())
            .ToArray();
        var existingBySourceIndex = existing.ToDictionary(row => row.SourceIndex!.Value);
        var importedSourceIndices = ordered.Select(chapter => chapter.Index).ToHashSet();
        var now = DateTimeOffset.UtcNow;

        foreach (var chapter in ordered) {
            if (existingBySourceIndex.TryGetValue(chapter.Index, out var marker)) {
                marker.Title = chapter.Title;
                marker.Seconds = chapter.StartSeconds;
                marker.EndSeconds = chapter.EndSeconds;
                marker.UpdatedAt = now;
                continue;
            }

            _db.EntityMarkers.Add(new EntityMarkerRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Title = chapter.Title,
                Seconds = chapter.StartSeconds,
                EndSeconds = chapter.EndSeconds,
                SourceIndex = chapter.Index,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        _db.EntityMarkers.RemoveRange(existing.Where(marker =>
            !importedSourceIndices.Contains(marker.SourceIndex!.Value)));

        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task<DirectEntityParentData?> GetDirectParentAsync(
        Guid entityId,
        CancellationToken cancellationToken) =>
        await _db.Entities.AsNoTracking()
            .Where(entity => entity.Id == entityId && entity.ParentEntityId != null)
            .Join(
                _db.Entities.AsNoTracking(),
                entity => entity.ParentEntityId,
                parent => parent.Id,
                (_, parent) => new DirectEntityParentData(parent.Id, parent.KindCode, parent.Title))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task MarkSubtitlesExtractedAsync(Guid entityId, CancellationToken cancellationToken) {
        var state = await _db.EntitySubtitleStates.FindAsync([entityId], cancellationToken);
        if (state is null) {
            state = new EntitySubtitleStateRow { EntityId = entityId };
            _db.EntitySubtitleStates.Add(state);
        }
        state.SubtitlesExtractedAt = DateTimeOffset.UtcNow;
        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task UpsertSubtitleAsync(Guid entityId, string language, string? label, string format,
        EntitySubtitleSource source, string storagePath, string sourceFormat, int streamIndex, CancellationToken cancellationToken) {
        var streamKey = SubtitleSourceKeys.EmbeddedStream(streamIndex);
        var streamIndexText = streamIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var existing = await _db.EntitySubtitles
            .FirstOrDefaultAsync(subtitle => subtitle.EntityId == entityId &&
                subtitle.Source == source &&
                (subtitle.SourceKey == streamKey ||
                    subtitle.SourceKey == string.Empty && subtitle.SourcePath == streamIndexText),
                cancellationToken);

        if (existing is null) {
            existing = new EntitySubtitleRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Source = source,
                SourceKey = streamKey,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.EntitySubtitles.Add(existing);
        }

        existing.SourceKey = streamKey;
        existing.Language = language;
        existing.Label = label;
        existing.Format = format;
        existing.StoragePath = storagePath;
        existing.SourceFormat = sourceFormat;
        existing.SourcePath = streamIndexText;

        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task<SubtitleReconciliationResult> ReconcileManagedSubtitlesAsync(
        Guid entityId,
        string sidecarSignature,
        IReadOnlyList<ManagedSubtitleTrackData> tracks,
        bool isComplete,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sidecarSignature);
        if (sidecarSignature.Length != 64 || !sidecarSignature.All(IsLowerHexCharacter)) {
            throw new ArgumentException("Subtitle sidecar signatures must be lowercase SHA-256 values.", nameof(sidecarSignature));
        }

        var managedSources = new[] { EntitySubtitleSource.Embedded, EntitySubtitleSource.Sidecar };
        var manifest = new Dictionary<(EntitySubtitleSource Source, string SourceKey), ManagedSubtitleTrackData>();
        foreach (var track in tracks) {
            if (!managedSources.Contains(track.Source)) {
                throw new ArgumentException("Only embedded and sidecar tracks may be reconciled by the extraction pipeline.", nameof(tracks));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(track.SourceKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(track.Language);
            ArgumentException.ThrowIfNullOrWhiteSpace(track.Format);
            ArgumentException.ThrowIfNullOrWhiteSpace(track.StoragePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(track.SourceFormat);
            if (track.SourceKey.Length > 128) {
                throw new ArgumentOutOfRangeException(nameof(tracks), "Subtitle source keys are limited to 128 characters.");
            }
            if (track.Language.Length > 32) {
                throw new ArgumentOutOfRangeException(nameof(tracks), "Subtitle language codes are limited to 32 characters.");
            }
            if (!Path.IsPathRooted(track.StoragePath)) {
                throw new ArgumentException("Managed subtitle storage paths must be absolute.", nameof(tracks));
            }
            if (track.Source == EntitySubtitleSource.Sidecar &&
                track.SourcePath is not null &&
                !Path.IsPathRooted(track.SourcePath)) {
                throw new ArgumentException("Styled sidecar source paths must be app-owned absolute paths.", nameof(tracks));
            }
            if (!manifest.TryAdd((track.Source, track.SourceKey), track)) {
                throw new ArgumentException("The subtitle manifest contains a duplicate source identity.", nameof(tracks));
            }
        }

        var existing = await _db.EntitySubtitles
            .Where(subtitle => subtitle.EntityId == entityId && managedSources.Contains(subtitle.Source))
            .ToListAsync(cancellationToken);
        var existingByKey = existing
            .Where(subtitle => !string.IsNullOrWhiteSpace(subtitle.SourceKey))
            .ToDictionary(subtitle => (subtitle.Source, subtitle.SourceKey));
        var obsoletePaths = new HashSet<string>(FileSystemPathComparison.Comparer);
        var retainedPaths = new HashSet<string>(FileSystemPathComparison.Comparer);

        foreach (var (identity, track) in manifest) {
            if (!existingByKey.TryGetValue(identity, out var row)) {
                row = new EntitySubtitleRow {
                    Id = Guid.NewGuid(),
                    EntityId = entityId,
                    Source = track.Source,
                    SourceKey = track.SourceKey,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _db.EntitySubtitles.Add(row);
            } else {
                AddManagedAssetPaths(row, obsoletePaths);
            }

            row.Language = track.Language;
            row.Label = track.Label;
            row.Format = track.Format;
            row.StoragePath = track.StoragePath;
            row.SourceFormat = track.SourceFormat;
            row.SourcePath = track.SourcePath;
            row.IsDefault = track.IsDefault;
            retainedPaths.Add(track.StoragePath);
            if (!string.IsNullOrWhiteSpace(track.SourcePath) && Path.IsPathRooted(track.SourcePath)) {
                retainedPaths.Add(track.SourcePath);
            }
        }

        foreach (var stale in existing.Where(row => !manifest.ContainsKey((row.Source, row.SourceKey)))) {
            AddManagedAssetPaths(stale, obsoletePaths);
            _db.EntitySubtitles.Remove(stale);
        }

        var state = await _db.EntitySubtitleStates.FindAsync([entityId], cancellationToken);
        if (state is null) {
            state = new EntitySubtitleStateRow { EntityId = entityId };
            _db.EntitySubtitleStates.Add(state);
        }
        state.SubtitleSidecarSignature = isComplete ? sidecarSignature : null;
        state.SubtitlesExtractedAt = isComplete ? DateTimeOffset.UtcNow : null;

        await SaveChangesWithLifecycleAsync(cancellationToken);
        obsoletePaths.ExceptWith(retainedPaths);
        return new SubtitleReconciliationResult(retainedPaths.ToArray(), obsoletePaths.ToArray());
    }

    private static void AddManagedAssetPaths(EntitySubtitleRow row, ISet<string> paths) {
        AddPath(row.StoragePath, paths);
        AddPath(row.SourcePath, paths);
        if (row.Source == EntitySubtitleSource.Embedded &&
            SubtitleFormats.IsStyled(row.SourceFormat) &&
            Path.IsPathRooted(row.StoragePath)) {
            AddPath(Path.ChangeExtension(
                row.StoragePath,
                SubtitleFileExtensions.ForFormat(row.SourceFormat)), paths);
        }
    }

    private static void AddPath(string? path, ISet<string> paths) {
        if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path)) {
            paths.Add(path);
        }
    }

    private static bool IsLowerHexCharacter(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';

    public async Task UpsertAudioTrackTagsAsync(Guid entityId, string? artist, string? album, int? trackNumber, CancellationToken cancellationToken) {
        var detail = await _db.AudioTrackDetails.FindAsync([entityId], cancellationToken);
        if (detail is null) return;

        if (artist is not null) detail.EmbeddedArtist = artist;
        if (album is not null) detail.EmbeddedAlbum = album;

        // Use the embedded track-number tag to set the album-global sort order, so identify can match
        // the track to its release track by position even when the filename is messy or unsorted. Only
        // for single-disc albums (one section), where the track number maps 1:1 to album-global order;
        // multi-disc albums keep their scanned order to avoid cross-disc position collisions.
        if (trackNumber is > 0 and var number) {
            var entity = await _db.Entities.FindAsync([entityId], cancellationToken);
            if (entity?.ParentEntityId is { } albumId) {
                var sectionCount = await _db.AudioTrackDetails.AsNoTracking()
                    .Where(d => _db.Entities.Any(e => e.Id == d.EntityId && e.ParentEntityId == albumId))
                    .Select(d => d.SectionOrder)
                    .Distinct()
                    .CountAsync(cancellationToken);
                if (sectionCount <= 1) {
                    entity.SortOrder = number - 1;
                }
            }
        }

        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task<EntityTechnicalData?> GetEntityTechnicalAsync(Guid entityId, CancellationToken cancellationToken) {
        var row = await _db.EntityTechnical.AsNoTracking()
            .FirstOrDefaultAsync(t => t.EntityId == entityId, cancellationToken);
        if (row is null) return null;

        return new EntityTechnicalData(row.DurationSeconds, row.Width, row.Height, row.FrameRate,
            row.BitRate, row.SampleRate, row.Channels, row.Codec, row.Container, row.ProbeFailedAt);
    }

    public async Task MarkEntityProbeFailedAsync(Guid entityId, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var existing = await _db.EntityTechnical.FindAsync([entityId], cancellationToken);
        if (existing is not null) {
            existing.ProbeFailedAt = now;
            existing.UpdatedAt = now;
        } else {
            _db.EntityTechnical.Add(new EntityTechnicalRow {
                EntityId = entityId,
                ProbeFailedAt = now,
                UpdatedAt = now
            });
        }

        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task ClearProbeFailuresForPathsAsync(
        IReadOnlyCollection<string> sourcePaths, CancellationToken cancellationToken) {
        var sourceEntityIds = await GetSourceEntityIdsForPathsAsync(sourcePaths, cancellationToken);
        if (sourceEntityIds.Length == 0) return;

        var rows = await _db.EntityTechnical
            .Where(technical => technical.ProbeFailedAt != null
                && sourceEntityIds.Contains(technical.EntityId))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows) {
            row.ProbeFailedAt = null;
            row.UpdatedAt = now;
        }

        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ClearManagedSubtitleCompletionForPathsAsync(
        IReadOnlyCollection<string> sourcePaths,
        CancellationToken cancellationToken) {
        var sourceEntityIds = await GetSourceEntityIdsForPathsAsync(sourcePaths, cancellationToken);
        if (sourceEntityIds.Length == 0) return;

        var rows = await _db.EntitySubtitleStates
            .Where(detail => sourceEntityIds.Contains(detail.EntityId) &&
                detail.SubtitlesExtractedAt != null)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return;

        foreach (var row in rows) {
            row.SubtitlesExtractedAt = null;
        }

        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    private async Task<Guid[]> GetSourceEntityIdsForPathsAsync(
        IReadOnlyCollection<string> sourcePaths,
        CancellationToken cancellationToken) {
        if (sourcePaths.Count == 0) return [];

        var paths = sourcePaths
            .Distinct(FileSystemPathComparison.Comparer)
            .ToHashSet(FileSystemPathComparison.Comparer);
        var pathLengths = paths.Select(path => path.Length).Distinct().ToArray();
        var sourceCandidates = await _db.EntityFiles.AsNoTracking()
            .Where(file => file.Role == EntityFileRole.Source &&
                pathLengths.Contains(file.Path.Length))
            .Select(file => new { file.EntityId, file.Path })
            .ToArrayAsync(cancellationToken);
        return sourceCandidates
            .Where(file => paths.Contains(file.Path))
            .Select(file => file.EntityId)
            .Distinct()
            .ToArray();
    }

}
