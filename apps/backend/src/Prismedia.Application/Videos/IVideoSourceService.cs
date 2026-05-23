namespace Prismedia.Application.Videos;

/// <summary>
/// Application port for locating original video source files that can be streamed by the API host.
/// </summary>
public interface IVideoSourceService {
    /// <summary>
    /// Finds the source file for one video entity.
    /// </summary>
    /// <param name="id">Video entity identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the lookup.</param>
    /// <returns>Source file metadata, or null when the video has no available source file.</returns>
    Task<VideoSourceFile?> GetSourceAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Source-file metadata needed by the API layer to serve direct video playback.
/// </summary>
/// <param name="EntityId">Video entity identifier that owns the source file.</param>
/// <param name="Path">Absolute path to the source file on disk.</param>
/// <param name="ContentType">HTTP content type for the source file.</param>
/// <param name="DirectPlayable">Whether the browser can play the source container directly.</param>
/// <param name="DurationSeconds">Optional probed duration used to build virtual HLS playlists.</param>
/// <param name="Width">Optional probed source width.</param>
/// <param name="Height">Optional probed source height.</param>
/// <param name="MediaSourceId">Optional persisted Jellyfin-style media source identifier.</param>
/// <param name="Container">Optional probed container name.</param>
/// <param name="BitRate">Optional probed aggregate bitrate.</param>
/// <param name="VideoCodec">Optional probed primary video codec.</param>
/// <param name="AudioCodec">Optional probed primary audio codec.</param>
/// <param name="FrameRate">Optional probed primary video frame rate.</param>
/// <param name="SampleRate">Optional probed primary audio sample rate.</param>
/// <param name="Channels">Optional probed primary audio channel count.</param>
/// <param name="Streams">Optional probed source streams available for playback selection.</param>
public sealed record VideoSourceFile(
    Guid EntityId,
    string Path,
    string ContentType,
    bool DirectPlayable,
    double? DurationSeconds = null,
    int? Width = null,
    int? Height = null,
    Guid? MediaSourceId = null,
    string? Container = null,
    int? BitRate = null,
    string? VideoCodec = null,
    string? AudioCodec = null,
    double? FrameRate = null,
    int? SampleRate = null,
    int? Channels = null,
    IReadOnlyList<VideoSourceStream>? Streams = null);

/// <summary>
/// Source stream metadata needed for HLS audio selection and Jellyfin-style playback info.
/// </summary>
/// <param name="StreamIndex">Absolute ffmpeg stream index within the source container.</param>
/// <param name="Type">Stream type, such as Video or Audio.</param>
/// <param name="Codec">Optional stream codec name.</param>
/// <param name="Language">Optional ISO language code.</param>
/// <param name="Title">Optional embedded stream title.</param>
/// <param name="Width">Optional video width.</param>
/// <param name="Height">Optional video height.</param>
/// <param name="FrameRate">Optional video frame rate.</param>
/// <param name="BitRate">Optional stream bitrate.</param>
/// <param name="SampleRate">Optional audio sample rate.</param>
/// <param name="Channels">Optional audio channel count.</param>
/// <param name="IsDefault">Whether the container marks this stream as default.</param>
/// <param name="IsForced">Whether the container marks this stream as forced.</param>
public sealed record VideoSourceStream(
    int StreamIndex,
    string Type,
    string? Codec,
    string? Language,
    string? Title,
    int? Width,
    int? Height,
    double? FrameRate,
    int? BitRate,
    int? SampleRate,
    int? Channels,
    bool IsDefault,
    bool IsForced,
    string? PixelFormat = null,
    int? BitDepth = null,
    string? ColorRange = null,
    string? ColorSpace = null,
    string? ColorTransfer = null,
    string? ColorPrimaries = null,
    int? DvProfile = null,
    int? DvLevel = null,
    bool? RpuPresentFlag = null,
    bool? ElPresentFlag = null,
    bool? BlPresentFlag = null,
    int? DvBlSignalCompatibilityId = null,
    bool Hdr10PlusPresentFlag = false);
