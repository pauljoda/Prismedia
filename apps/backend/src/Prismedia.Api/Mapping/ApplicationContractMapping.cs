using Prismedia.Application.Videos;
using Prismedia.Contracts.Playback;

namespace Prismedia.Api.Mapping;

internal static class ApplicationContractMapping {
    public static PlaybackInfoQuery ToApplication(this PlaybackInfoRequest request) =>
        new() {
            UserId = request.UserId,
            StartTimeTicks = request.StartTimeTicks,
            AudioStreamIndex = request.AudioStreamIndex,
            SubtitleStreamIndex = request.SubtitleStreamIndex,
            MaxStreamingBitrate = request.MaxStreamingBitrate,
            EnableDirectPlay = request.EnableDirectPlay,
            EnableDirectStream = request.EnableDirectStream,
            EnableTranscoding = request.EnableTranscoding,
            MediaSourceId = request.MediaSourceId,
            PlaySessionId = request.PlaySessionId,
            SupportedVideoRangeTypes = request.SupportedVideoRangeTypes
        };

    public static PlaybackSessionCommand ToApplication(this PlaybackSessionRequest request) =>
        new() {
            ItemId = request.ItemId,
            MediaSourceId = request.MediaSourceId,
            PlaySessionId = request.PlaySessionId,
            PositionTicks = request.PositionTicks,
            IsPaused = request.IsPaused,
            IsMuted = request.IsMuted
        };

    public static PlaybackInfoResponse ToContract(this PlaybackInfoResult result) =>
        new(result.PlaySessionId, result.MediaSources.Select(ToContract).ToArray(), result.ErrorCode);

    private static MediaSourceInfo ToContract(MediaSourceInfoResult result) =>
        new(
            result.Id,
            result.Path,
            result.Protocol,
            result.Container,
            result.Size,
            result.Name,
            result.RunTimeTicks,
            result.SupportsDirectPlay,
            result.SupportsDirectStream,
            result.SupportsTranscoding,
            result.TranscodingUrl,
            result.TranscodingSubProtocol,
            result.TranscodingContainer,
            result.MediaStreams.Select(ToContract).ToArray(),
            result.TranscodingInfo?.ToContract());

    private static MediaStreamInfo ToContract(MediaStreamInfoResult result) =>
        new(
            result.Index,
            result.Type,
            result.Codec,
            result.Language,
            result.DisplayTitle,
            result.Width,
            result.Height,
            result.AverageFrameRate,
            result.BitRate,
            result.SampleRate,
            result.Channels,
            result.IsDefault,
            result.IsForced,
            result.VideoRange,
            result.VideoRangeType,
            result.PixelFormat,
            result.BitDepth,
            result.ColorRange,
            result.ColorSpace,
            result.ColorTransfer,
            result.ColorPrimaries,
            result.DvProfile,
            result.DvLevel,
            result.RpuPresentFlag,
            result.ElPresentFlag,
            result.BlPresentFlag,
            result.DvBlSignalCompatibilityId,
            result.Hdr10PlusPresentFlag);

    private static TranscodingInfo ToContract(this TranscodingInfoResult result) =>
        new(
            result.Container,
            result.VideoCodec,
            result.AudioCodec,
            result.Protocol,
            result.IsVideoDirect,
            result.IsAudioDirect);

    public static UserItemData ToContract(this UserItemDataResult result) =>
        new(result.Played, result.PlayCount, result.PlaybackPositionTicks);

}
