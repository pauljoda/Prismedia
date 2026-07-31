using Prismedia.Application.Videos;
using Prismedia.Contracts.Playback;

namespace Prismedia.Api.Mapping;

internal static class VideoPlaybackMapping {
    internal static VideoPlaybackPlanQuery ToApplication(this VideoPlaybackPlanRequest request) =>
        new() {
            AudioStreamIndex = request.AudioStreamIndex,
            EnableDirectPlay = request.EnableDirectPlay,
            EnableDirectStream = request.EnableDirectStream,
            EnableTranscoding = request.EnableTranscoding,
            EnableClientToneMapping = request.EnableClientToneMapping,
            SessionId = request.SessionId,
            SupportedVideoRangeTypes = request.SupportedVideoRangeTypes,
            Profile = request.Profile.ToApplication()
        };

    internal static VideoPlaybackSessionCommand ToApplication(this VideoPlaybackSessionRequest request) =>
        new() {
            EntityId = request.EntityId,
            SessionId = request.SessionId,
            PositionSeconds = request.PositionSeconds,
            DurationSeconds = request.DurationSeconds,
            Completed = request.Completed
        };

    internal static VideoPlaybackPlanResponse ToContract(this VideoPlaybackPlanResult result) =>
        new(result.SessionId, result.Source.ToContract());

    private static VideoPlaybackProfile? ToApplication(this VideoPlaybackProfileRequest? profile) =>
        profile is null
            ? null
            : new VideoPlaybackProfile(
                profile.MaxStreamingBitrate,
                profile.DirectPlayProfiles?
                    .Select(entry => new VideoDirectPlayProfile(
                        entry.Type,
                        entry.Container,
                        entry.VideoCodec,
                        entry.AudioCodec))
                    .ToArray() ?? []);

    private static VideoPlaybackSource ToContract(this VideoPlaybackSourceResult source) =>
        new(
            source.Id,
            source.Container,
            source.DurationSeconds,
            source.Method,
            source.Url,
            source.SupportsTranscoding,
            source.Streams.Select(ToContract).ToArray(),
            source.Transcoding?.ToContract());

    private static VideoPlaybackStream ToContract(VideoPlaybackStreamResult stream) =>
        new(
            stream.Index,
            stream.Type,
            stream.Codec,
            stream.Language,
            stream.DisplayTitle,
            stream.Width,
            stream.Height,
            stream.AverageFrameRate,
            stream.BitRate,
            stream.SampleRate,
            stream.Channels,
            stream.IsDefault,
            stream.IsForced,
            stream.VideoRange,
            stream.VideoRangeType,
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
            stream.Hdr10PlusPresentFlag);

    private static VideoTranscodingInfo ToContract(this VideoTranscodingResult transcoding) =>
        new(
            transcoding.Container,
            transcoding.VideoCodec,
            transcoding.AudioCodec,
            transcoding.IsVideoDirect,
            transcoding.IsAudioDirect);
}
