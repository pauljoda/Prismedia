using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Videos;
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
                entity.DeletedAt == null &&
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
        if (_mediaProbe is not null && ShouldProbeStreams(source.File.Path, mediaSource?.VideoCodec ?? source.Technical?.Codec, streams)) {
            var probed = await _mediaProbe.ProbeVideoAsync(source.File.Path, cancellationToken);
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
            mediaSource?.DurationSeconds ?? source.Technical?.DurationSeconds,
            mediaSource?.Width ?? source.Technical?.Width,
            mediaSource?.Height ?? source.Technical?.Height,
            mediaSource?.Id,
            mediaSource?.Container ?? source.Technical?.Container,
            mediaSource?.BitRate ?? source.Technical?.BitRate,
            mediaSource?.VideoCodec ?? source.Technical?.Codec,
            mediaSource?.AudioCodec,
            mediaSource?.FrameRate ?? source.Technical?.FrameRate,
            source.Technical?.SampleRate,
            source.Technical?.Channels,
            streams);
    }

    /// <summary>
    /// Resolves the id whose source file should be streamed: a video id maps to itself, a movie id
    /// maps to its single playable video child, and anything else (or a missing entity) yields null.
    /// </summary>
    private async Task<Guid?> ResolvePlayableVideoIdAsync(Guid id, CancellationToken cancellationToken) {
        var kind = await _db.Entities.AsNoTracking()
            .Where(entity => entity.Id == id && entity.DeletedAt == null)
            .Select(entity => entity.KindCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(kind, EntityKindRegistry.Video.Code, StringComparison.Ordinal)) {
            return id;
        }

        if (string.Equals(kind, EntityKindRegistry.Movie.Code, StringComparison.Ordinal)) {
            return await _db.Entities.AsNoTracking()
                .Where(child => child.ParentEntityId == id &&
                    child.KindCode == EntityKindRegistry.Video.Code &&
                    child.DeletedAt == null)
                .OrderBy(child => child.SortOrder ?? int.MaxValue)
                .ThenBy(child => child.Id)
                .Select(child => (Guid?)child.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    private static string MimeForExtension(string extension) {
        return extension.ToLowerInvariant() switch {
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".ogg" or ".ogv" => "video/ogg",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".wmv" => "video/x-ms-wmv",
            ".flv" => "video/x-flv",
            ".ts" or ".m2ts" => "video/mp2t",
            _ => "application/octet-stream"
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
