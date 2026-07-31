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
        // its child video before locating the source. This lets native clients stream, plan
        // playback, and fetch HLS using the movie's own id (all three funnel through here).
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
                entity.KindCode == EntityKind.Video.ToCode() &&
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
        var actualSizeBytes = new FileInfo(source.File.Path).Length;
        var sourceFileChanged = mediaSource?.SizeBytes is { } probedSizeBytes &&
            probedSizeBytes != actualSizeBytes;
        VideoProbeResult? probed = null;
        if (_mediaProbe is not null &&
            (sourceFileChanged || ShouldProbeStreams(source.File.Path, mediaSource?.VideoCodec ?? source.Technical?.Codec, streams))) {
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
        // A file may be replaced in place between scans. When its observed size no longer matches the
        // persisted probe, the fresh probe must own both the stream map and the top-level metadata;
        // otherwise playback can select an old audio index that now points at a subtitle stream.
        var preferFreshProbe = sourceFileChanged && probed is not null;
        var extension = Path.GetExtension(source.File.Path);
        var directPlayable =
            BrowserNativeExtensions.Contains(extension) ||
            !RequiresTranscodeExtensions.Contains(extension);
        var durationSeconds = mediaSource?.DurationSeconds ?? source.Technical?.DurationSeconds ?? probed?.DurationSeconds;
        var width = mediaSource?.Width ?? source.Technical?.Width ?? probed?.Width;
        var height = mediaSource?.Height ?? source.Technical?.Height ?? probed?.Height;
        var container = mediaSource?.Container ?? source.Technical?.Container ?? probed?.Container;
        var bitRate = mediaSource?.BitRate ?? source.Technical?.BitRate ?? probed?.BitRate;
        var videoCodec = mediaSource?.VideoCodec ?? source.Technical?.Codec ?? probed?.Codec;
        var audioCodec = mediaSource?.AudioCodec ?? probed?.AudioCodec;
        var frameRate = mediaSource?.FrameRate ?? source.Technical?.FrameRate ?? probed?.FrameRate;
        var sampleRate = source.Technical?.SampleRate ?? probed?.SampleRate;
        var channels = source.Technical?.Channels ?? probed?.Channels;
        if (preferFreshProbe) {
            durationSeconds = probed!.DurationSeconds ?? durationSeconds;
            width = probed.Width ?? width;
            height = probed.Height ?? height;
            container = probed.Container ?? container;
            bitRate = probed.BitRate ?? bitRate;
            videoCodec = probed.Codec ?? videoCodec;
            audioCodec = probed.AudioCodec ?? audioCodec;
            frameRate = probed.FrameRate ?? frameRate;
            sampleRate = probed.SampleRate ?? sampleRate;
            channels = probed.Channels ?? channels;
        }

        return new VideoSourceFile(
            videoId.Value,
            source.File.Path,
            source.File.MimeType ?? MimeForExtension(extension),
            directPlayable,
            durationSeconds,
            width,
            height,
            mediaSource?.Id,
            container,
            bitRate,
            videoCodec,
            audioCodec,
            frameRate,
            sampleRate,
            channels,
            streams);
    }

    /// <summary>
    /// Resolves the id whose source file should be streamed: a video id maps to itself, a movie id
    /// maps to its single playable video child, and anything else (or a missing entity) yields null.
    /// </summary>
    private async Task<Guid?> ResolvePlayableVideoIdAsync(Guid id, CancellationToken cancellationToken) {
        var kind = await _db.Entities.AsNoTracking()
            .Where(entity => entity.Id == id)
            .Select(entity => entity.KindCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(kind, EntityKind.Video.ToCode(), StringComparison.Ordinal)) {
            return id;
        }

        if (string.Equals(kind, EntityKind.Movie.ToCode(), StringComparison.Ordinal)) {
            return await _db.Entities.AsNoTracking()
                .Where(child => child.ParentEntityId == id &&
                    child.KindCode == EntityKind.Video.ToCode())
                .OrderBy(child => child.SortOrder ?? int.MaxValue)
                .ThenBy(child => child.Id)
                .Select(child => (Guid?)child.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
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
        if (streams.Count(stream => stream.Type.Equals(StreamKind.Audio.ToCode(), StringComparison.OrdinalIgnoreCase)) <= 1) {
            return true;
        }

        var primaryVideo = streams
            .Where(stream => stream.Type.Equals(StreamKind.Video.ToCode(), StringComparison.OrdinalIgnoreCase))
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

    // HDR-prone and bandwidth-efficient are the same modern-codec set today; the shared
    // predicate owns the membership while this name keeps the local domain meaning.
    private static bool IsHdrProneCodec(string? codec) =>
        MediaCodecs.IsEfficientVideoCodec(codec);
}
