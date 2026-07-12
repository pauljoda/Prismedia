using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// EF-backed implementation that resolves source video files from the shared file capability table.
/// </summary>
public sealed class VideoSourceService : IVideoSourceService {
    private static readonly ISet<string> BrowserNativeExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4",
            ".webm",
            ".ogg",
            ".ogv",
            ".m4v"
        };

    private static readonly ISet<string> RequiresTranscodeExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv",
            ".avi",
            ".wmv",
            ".flv",
            ".mov",
            ".ts",
            ".m2ts"
        };

    private readonly PrismediaDbContext _db;
    private readonly MediaProbeService? _mediaProbe;

    /// <summary>
    /// Creates a video source resolver over the database context.
    /// </summary>
    /// <param name="db">Database context used to find video source file rows.</param>
    public VideoSourceService(PrismediaDbContext db, MediaProbeService? mediaProbe = null) {
        _db = db;
        _mediaProbe = mediaProbe;
    }

    /// <inheritdoc />
    public async Task<VideoSourceFile?> GetSourceAsync(Guid id, CancellationToken cancellationToken) {
        // A movie is a folder aggregate around one playable video child, so resolve a movie id to
        // its child video before locating the source. This lets Jellyfin clients stream, probe
        // playback info, and fetch HLS using the movie's own id (all three funnel through here).
        var videoId = await ResolvePlayableVideoIdAsync(id, cancellationToken);
        if (videoId is null) {
            return null;
        }

        var source = await (
            from entity in _db.Entities.AsNoTracking()
            join file in _db.EntityFiles.AsNoTracking() on entity.Id equals file.EntityId
            join technical in _db.EntityTechnical.AsNoTracking() on entity.Id equals technical.EntityId into technicalRows
            from technical in technicalRows.DefaultIfEmpty()
            where entity.Id == videoId.Value &&
                entity.KindCode == EntityKindRegistry.Video.Code &&
                file.Role == EntityFileRole.Source
            select new {
                File = file,
                Technical = technical
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (source is null || !File.Exists(source.File.Path)) {
            return null;
        }

        var mediaSource = await _db.MediaSources.AsNoTracking()
            .Where(row => row.EntityId == videoId.Value && row.Path == source.File.Path)
            .OrderByDescending(row => row.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        List<VideoSourceStream> streams = mediaSource is null
            ? []
            : await _db.MediaStreams.AsNoTracking()
                .Where(row => row.MediaSourceId == mediaSource.Id)
                .OrderBy(row => row.StreamIndex)
                .Select(row => new VideoSourceStream(
                    row.StreamIndex,
                    row.Type,
                    row.Codec,
                    row.Language,
                    row.Title,
                    row.Width,
                    row.Height,
                    row.FrameRate,
                    row.BitRate,
                    row.SampleRate,
                    row.Channels,
                    row.IsDefault,
                    row.IsForced,
                    row.PixelFormat,
                    row.BitDepth,
                    row.ColorRange,
                    row.ColorSpace,
                    row.ColorTransfer,
                    row.ColorPrimaries,
                    row.DvProfile,
                    row.DvLevel,
                    row.RpuPresentFlag,
                    row.ElPresentFlag,
                    row.BlPresentFlag,
                    row.DvBlSignalCompatibilityId,
                    row.Hdr10PlusPresentFlag))
                .ToListAsync(cancellationToken);
        VideoProbeResult? probed = null;
        if (_mediaProbe is not null && ShouldProbeStreams(source.File.Path, mediaSource?.VideoCodec ?? source.Technical?.Codec, streams)) {
            probed = await _mediaProbe.ProbeVideoAsync(source.File.Path, cancellationToken);
            if (probed?.Streams is { Count: > 0 }) {
                streams = probed.Streams
                    .Select(stream => new VideoSourceStream(
                        stream.StreamIndex,
                        stream.Type,
                        stream.Codec,
                        stream.Language,
                        stream.Title,
                        stream.Width,
                        stream.Height,
                        stream.FrameRate,
                        stream.BitRate,
                        stream.SampleRate,
                        stream.Channels,
                        stream.IsDefault,
                        stream.IsForced,
                        stream.PixelFormat,
                        stream.BitDepth,
                        stream.ColorRange,
                        stream.ColorSpace,
                        stream.ColorTransfer,
                        stream.ColorPrimaries,
                        stream.DvProfile,
                        stream.DvLevel,
                        stream.RpuPresentFlag,
                        stream.ElPresentFlag,
                        stream.BlPresentFlag,
                        stream.DvBlSignalCompatibilityId,
                        stream.Hdr10PlusPresentFlag))
                    .OrderBy(stream => stream.StreamIndex)
                    .ToList();
            }
        }
        var extension = Path.GetExtension(source.File.Path);
        var directPlayable =
            BrowserNativeExtensions.Contains(extension) ||
            !RequiresTranscodeExtensions.Contains(extension);

        return new VideoSourceFile(
            videoId.Value,
            source.File.Path,
            source.File.MimeType ?? MimeForExtension(extension),
            directPlayable,
            mediaSource?.DurationSeconds ?? source.Technical?.DurationSeconds ?? probed?.DurationSeconds,
            mediaSource?.Width ?? source.Technical?.Width ?? probed?.Width,
            mediaSource?.Height ?? source.Technical?.Height ?? probed?.Height,
            mediaSource?.Id,
            mediaSource?.Container ?? source.Technical?.Container ?? probed?.Container,
            mediaSource?.BitRate ?? source.Technical?.BitRate ?? probed?.BitRate,
            mediaSource?.VideoCodec ?? source.Technical?.Codec ?? probed?.Codec,
            mediaSource?.AudioCodec ?? probed?.AudioCodec,
            mediaSource?.FrameRate ?? source.Technical?.FrameRate ?? probed?.FrameRate,
            source.Technical?.SampleRate ?? probed?.SampleRate,
            source.Technical?.Channels ?? probed?.Channels,
            streams);
    }

    /// <summary>
    /// Resolves the id whose source file should be streamed: a video id maps to itself; a movie,
    /// series, or season id maps to its first playable video descendant (walking through a series'
    /// season folder when it has one); anything else (or a missing entity) yields null. Series and
    /// season containers get the same fallback as movies so clicking into one never dead-ends just
    /// because it groups several loose video files instead of a single one.
    /// </summary>
    private async Task<Guid?> ResolvePlayableVideoIdAsync(Guid id, CancellationToken cancellationToken) {
        var kind = await _db.Entities.AsNoTracking()
            .Where(entity => entity.Id == id)
            .Select(entity => entity.KindCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(kind, EntityKindRegistry.Video.Code, StringComparison.Ordinal)) {
            return id;
        }

        if (string.Equals(kind, EntityKindRegistry.Movie.Code, StringComparison.Ordinal) ||
            string.Equals(kind, EntityKindRegistry.VideoSeason.Code, StringComparison.Ordinal)) {
            return await FirstChildVideoIdAsync(id, cancellationToken);
        }

        if (string.Equals(kind, EntityKindRegistry.VideoSeries.Code, StringComparison.Ordinal)) {
            var children = await _db.Entities.AsNoTracking()
                .Where(child => child.ParentEntityId == id)
                .OrderBy(child => child.SortOrder ?? int.MaxValue)
                .ThenBy(child => child.Id)
                .Select(child => new { child.Id, child.KindCode })
                .ToListAsync(cancellationToken);

            // A series' direct children are videos when it's a flat, unnumbered folder of loose
            // clips, or season containers when the release is season-structured, so both shapes
            // need to be resolved to reach an actual playable video. Keep walking past a season
            // that turns out to hold no videos (e.g. a partially scanned library) instead of
            // dead-ending on it, since a later season may still be playable.
            foreach (var child in children) {
                var videoId = string.Equals(child.KindCode, EntityKindRegistry.Video.Code, StringComparison.Ordinal)
                    ? child.Id
                    : await FirstChildVideoIdAsync(child.Id, cancellationToken);
                if (videoId is not null) {
                    return videoId;
                }
            }

            return null;
        }

        return null;
    }

    private async Task<Guid?> FirstChildVideoIdAsync(Guid parentId, CancellationToken cancellationToken) {
        return await _db.Entities.AsNoTracking()
            .Where(child => child.ParentEntityId == parentId &&
                child.KindCode == EntityKindRegistry.Video.Code)
            .OrderBy(child => child.SortOrder ?? int.MaxValue)
            .ThenBy(child => child.Id)
            .Select(child => (Guid?)child.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string MimeForExtension(string extension) {
        return extension.ToLowerInvariant() switch {
            ".mp4" or ".m4v" => MediaContentTypes.VideoMp4,
            ".webm" => MediaContentTypes.VideoWebm,
            ".ogg" or ".ogv" => MediaContentTypes.VideoOgg,
            ".mov" => MediaContentTypes.VideoQuicktime,
            ".mkv" => MediaContentTypes.VideoMatroska,
            ".avi" => MediaContentTypes.VideoAvi,
            ".wmv" => MediaContentTypes.VideoWmv,
            ".flv" => MediaContentTypes.VideoFlv,
            ".ts" or ".m2ts" => MediaContentTypes.VideoMp2t,
            _ => MediaContentTypes.OctetStream
        };
    }

    private static bool ShouldProbeStreams(
        string path,
        string? videoCodec,
        IReadOnlyList<VideoSourceStream> streams) {
        if (streams.Count(stream => stream.Type.Equals("Audio", StringComparison.OrdinalIgnoreCase)) <= 1) {
            return true;
        }

        var primaryVideo = streams
            .Where(stream => stream.Type.Equals("Video", StringComparison.OrdinalIgnoreCase))
            .OrderBy(stream => stream.StreamIndex)
            .FirstOrDefault();
        if (primaryVideo is null) {
            return true;
        }

        var codec = primaryVideo.Codec ?? videoCodec;
        if (!IsHdrProneCodec(codec) && !Path.GetExtension(path).Equals(".mkv", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return primaryVideo.PixelFormat is null &&
            primaryVideo.BitDepth is null &&
            primaryVideo.ColorTransfer is null &&
            primaryVideo.ColorPrimaries is null &&
            primaryVideo.DvProfile is null &&
            primaryVideo.RpuPresentFlag is null &&
            !primaryVideo.Hdr10PlusPresentFlag;
    }

    private static bool IsHdrProneCodec(string? codec) =>
        codec is not null && (
            codec.Equals("hevc", StringComparison.OrdinalIgnoreCase) ||
            codec.Equals("h265", StringComparison.OrdinalIgnoreCase) ||
            codec.Equals("av1", StringComparison.OrdinalIgnoreCase) ||
            codec.Equals("vp9", StringComparison.OrdinalIgnoreCase));
}
