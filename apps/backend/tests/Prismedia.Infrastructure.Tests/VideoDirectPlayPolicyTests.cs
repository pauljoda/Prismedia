using Prismedia.Application.Videos;

namespace Prismedia.Infrastructure.Tests;

public sealed class VideoDirectPlayPolicyTests {
    // A client (Infuse / Apple TV class) that can directly play HEVC/H.264 and EAC3/Atmos in MKV.
    private static readonly ClientPlaybackProfile CapableClient = new(
        MaxStreamingBitrate: 200_000_000,
        DirectPlayProfiles:
        [
            new ClientDirectPlayProfile("Video", "mkv,mp4,ts,mov", "hevc,h264,av1", "eac3,ac3,aac,truehd,dts")
        ]);

    private static VideoSourceFile Source(
        string path,
        string container,
        string videoCodec,
        string? audioCodec,
        bool directPlayable = false,
        int? bitRate = null) =>
        new(
            Guid.NewGuid(),
            path,
            "video/x-matroska",
            directPlayable,
            Container: container,
            BitRate: bitRate,
            VideoCodec: videoCodec,
            AudioCodec: audioCodec);

    // A Dolby Vision MKV whose primary video stream carries the DV profile/base-layer fields the
    // remux gate reads to decide whether a stream copy can render correctly on a non-DV client.
    private static VideoSourceFile DolbyVisionSource(
        string path,
        int dvProfile,
        int dvBlSignalCompatibilityId) =>
        new(
            Guid.NewGuid(),
            path,
            "video/x-matroska",
            DirectPlayable: false,
            Container: "matroska",
            VideoCodec: "hevc",
            AudioCodec: "eac3",
            Streams:
            [
                new VideoSourceStream(
                    StreamIndex: 0,
                    Type: "Video",
                    Codec: "hevc",
                    Language: null,
                    Title: null,
                    Width: 3840,
                    Height: 2160,
                    FrameRate: 24,
                    BitRate: null,
                    SampleRate: null,
                    Channels: null,
                    IsDefault: true,
                    IsForced: false,
                    DvProfile: dvProfile,
                    DvBlSignalCompatibilityId: dvBlSignalCompatibilityId),
                new VideoSourceStream(
                    StreamIndex: 1,
                    Type: "Audio",
                    Codec: "eac3",
                    Language: "eng",
                    Title: null,
                    Width: null,
                    Height: null,
                    FrameRate: null,
                    BitRate: null,
                    SampleRate: 48000,
                    Channels: 6,
                    IsDefault: true,
                    IsForced: false),
            ]);

    private static VideoSourceFile Hdr10Source(string path, int bitDepth) =>
        new(
            Guid.NewGuid(),
            path,
            "video/x-matroska",
            DirectPlayable: false,
            Container: "matroska",
            VideoCodec: "hevc",
            AudioCodec: "eac3",
            Streams:
            [
                new VideoSourceStream(
                    StreamIndex: 0,
                    Type: "Video",
                    Codec: "hevc",
                    Language: null,
                    Title: null,
                    Width: 1920,
                    Height: 1080,
                    FrameRate: 24,
                    BitRate: null,
                    SampleRate: null,
                    Channels: null,
                    IsDefault: true,
                    IsForced: false,
                    PixelFormat: bitDepth >= 10 ? "yuv420p10le" : "yuv420p",
                    BitDepth: bitDepth,
                    ColorSpace: "bt2020nc",
                    ColorTransfer: "smpte2084",
                    ColorPrimaries: "bt2020")
            ]);

    [Fact]
    public void CapableClientDirectPlaysDolbyVisionHevcAtmosMkv() {
        var source = Source("/media/e02.mkv", "matroska", "hevc", "eac3");

        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "eac3",
            range: VideoPlaybackRange.Dovi,
            profile: CapableClient,
            supportedVideoRangeTypes: ["DOVI", "DOVIWithHDR10", "HDR10"],
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true);

        Assert.Equal(VideoPlaybackMethod.DirectPlay, decision.Method);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    public void SubTenBitHdrTranscodesRatherThanCopyingMalformedHdr(int bitDepth) {
        var source = Hdr10Source("/media/malformed-hdr.mkv", bitDepth);

        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "eac3",
            range: VideoPlaybackRange.Hdr10,
            profile: CapableClient,
            supportedVideoRangeTypes: ["HDR10"],
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true);

        Assert.Equal(VideoPlaybackMethod.Transcode, decision.Method);
    }

    [Fact]
    public void SubTenBitHdrDirectPlaysWhenClientExplicitlySupportsLocalToneMapping() {
        var source = Hdr10Source("/media/malformed-hdr.mkv", bitDepth: 8);

        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "eac3",
            range: VideoPlaybackRange.Hdr10,
            profile: CapableClient,
            supportedVideoRangeTypes: ["HDR10"],
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true,
            clientToneMappingAllowed: true);

        Assert.Equal(VideoPlaybackMethod.DirectPlay, decision.Method);
    }

    [Fact]
    public void TenBitHdrRemainsEligibleForStreamCopy() {
        var source = Hdr10Source("/media/valid-hdr.mkv", bitDepth: 10);

        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "eac3",
            range: VideoPlaybackRange.Hdr10,
            profile: CapableClient,
            supportedVideoRangeTypes: ["HDR10"],
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true);

        Assert.Equal(VideoPlaybackMethod.DirectPlay, decision.Method);
    }

    [Fact]
    public void DolbyVisionRemuxesToAnHevcClientEvenWithoutAdvertisedRangeSupport() {
        var source = Source("/media/e02.mkv", "matroska", "hevc", "eac3");

        // A Dolby Vision MKV to a client that decodes HEVC: the container (mkv) is not directly
        // playable, but the video is copied unchanged into fMP4 — no tone map — so the client renders
        // the HDR/DV stream itself. The client need not advertise the dynamic range for a copy.
        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "eac3",
            range: VideoPlaybackRange.Dovi,
            profile: CapableClient,
            supportedVideoRangeTypes: ["SDR", "HDR10"],
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true);

        Assert.Equal(VideoPlaybackMethod.Remux, decision.Method);
    }

    [Fact]
    public void DolbyVisionProfile5TranscodesRatherThanCopyingToAClientThatCannotRenderIt() {
        // Profile 5 has an ICtCp base layer with no HDR10 fallback. A browser-class client decodes the
        // HEVC bitstream without Dolby Vision processing and shows a magenta/green cast, so the policy
        // must tone-map (transcode) instead of stream-copying, even though the client decodes HEVC.
        var browserClient = new ClientPlaybackProfile(
            MaxStreamingBitrate: null,
            DirectPlayProfiles: [new ClientDirectPlayProfile("Video", "mp4,webm", "hevc,h264,av1", "aac,opus")]);
        var source = DolbyVisionSource("/media/p5.mkv", dvProfile: 5, dvBlSignalCompatibilityId: 0);

        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "eac3",
            range: VideoPlaybackRange.Dovi,
            profile: browserClient,
            supportedVideoRangeTypes: ["SDR", "HDR10"],
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true);

        Assert.Equal(VideoPlaybackMethod.Transcode, decision.Method);
    }

    [Fact]
    public void DolbyVisionProfile5RemuxesToAClientThatAdvertisesDolbyVision() {
        // A real Dolby Vision client (advertises DOVI) processes the RPU itself, so copying Profile 5
        // unchanged is correct — the tone-map gate only blocks copies to clients that cannot render it.
        // The client decodes HEVC only in mp4 (not the source's mkv container), so the verdict is a
        // Remux rather than a DirectPlay.
        var source = DolbyVisionSource("/media/p5.mkv", dvProfile: 5, dvBlSignalCompatibilityId: 0);

        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "eac3",
            range: VideoPlaybackRange.Dovi,
            profile: new ClientPlaybackProfile(
                MaxStreamingBitrate: null,
                DirectPlayProfiles: [new ClientDirectPlayProfile("Video", "mp4", "hevc", "aac")]),
            supportedVideoRangeTypes: ["DOVI", "HDR10"],
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true);

        Assert.Equal(VideoPlaybackMethod.Remux, decision.Method);
    }

    [Fact]
    public void DolbyVisionProfile8WithHdr10BaseLayerRemuxesToAnHevcClient() {
        // Profile 8.1 carries an HDR10-compatible base layer (compatibility id 1); a client that decodes
        // HEVC renders the copied base layer correctly as HDR10, so a stream copy stays valid.
        var profile = new ClientPlaybackProfile(
            MaxStreamingBitrate: null,
            DirectPlayProfiles: [new ClientDirectPlayProfile("Video", "mp4", "hevc", "aac")]);
        var source = DolbyVisionSource("/media/p8.mkv", dvProfile: 8, dvBlSignalCompatibilityId: 1);

        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "eac3",
            range: VideoPlaybackRange.Dovi,
            profile: profile,
            supportedVideoRangeTypes: ["SDR", "HDR10"],
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true);

        Assert.Equal(VideoPlaybackMethod.Remux, decision.Method);
    }

    [Fact]
    public void DolbyVisionTranscodesWhenClientCannotDecodeTheVideoCodec() {
        var source = Source("/media/e02.mkv", "matroska", "hevc", "eac3");

        // A browser-class client that can only decode H.264 cannot copy the HEVC stream, so the only
        // option is a full transcode (which tone-maps the HDR/DV down to SDR H.264).
        var h264OnlyClient = new ClientPlaybackProfile(
            MaxStreamingBitrate: null,
            DirectPlayProfiles: [new ClientDirectPlayProfile("Video", "mp4", "h264", "aac")]);

        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "eac3",
            range: VideoPlaybackRange.Dovi,
            profile: h264OnlyClient,
            supportedVideoRangeTypes: ["SDR", "HDR10"],
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true);

        Assert.Equal(VideoPlaybackMethod.Transcode, decision.Method);
    }

    [Fact]
    public void RemuxesWhenVideoCodecPlayableButContainerUnsupported() {
        // Client plays HEVC only in mp4 and only AAC audio.
        var profile = new ClientPlaybackProfile(
            MaxStreamingBitrate: null,
            DirectPlayProfiles: [new ClientDirectPlayProfile("Video", "mp4", "hevc", "aac")]);
        var source = Source("/media/clip.mkv", "matroska", "hevc", "eac3");

        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "eac3",
            range: VideoPlaybackRange.Sdr,
            profile: profile,
            supportedVideoRangeTypes: null,
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true);

        Assert.Equal(VideoPlaybackMethod.Remux, decision.Method);
        Assert.Equal("mp4", decision.RemuxContainer);
        Assert.False(decision.CopyAudio); // EAC3 not in the client's audio list, so audio is transcoded
    }

    [Fact]
    public void RemuxCopiesAudioWhenClientAlsoSupportsTheAudioCodec() {
        var profile = new ClientPlaybackProfile(
            MaxStreamingBitrate: null,
            DirectPlayProfiles: [new ClientDirectPlayProfile("Video", "mp4", "hevc", "aac,eac3")]);
        var source = Source("/media/clip.mkv", "matroska", "hevc", "eac3");

        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "eac3",
            range: VideoPlaybackRange.Sdr,
            profile: profile,
            supportedVideoRangeTypes: null,
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true);

        Assert.Equal(VideoPlaybackMethod.Remux, decision.Method);
        Assert.True(decision.CopyAudio);
    }

    [Fact]
    public void BitrateCeilingForcesTranscode() {
        var profile = new ClientPlaybackProfile(
            MaxStreamingBitrate: 8_000_000,
            DirectPlayProfiles: [new ClientDirectPlayProfile("Video", "mkv", "hevc", "eac3")]);
        var source = Source("/media/e02.mkv", "matroska", "hevc", "eac3", bitRate: 40_000_000);

        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "eac3",
            range: VideoPlaybackRange.Sdr,
            profile: profile,
            supportedVideoRangeTypes: null,
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true);

        Assert.Equal(VideoPlaybackMethod.Transcode, decision.Method);
    }

    [Theory]
    [InlineData(true, VideoPlaybackMethod.DirectPlay)]
    [InlineData(false, VideoPlaybackMethod.Transcode)]
    public void WithoutDeviceProfileFallsBackToContainerExtensionHeuristic(
        bool directPlayable,
        VideoPlaybackMethod expected) {
        var source = Source("/media/clip.ext", "matroska", "h264", "aac", directPlayable: directPlayable);

        var decision = VideoDirectPlayPolicy.Decide(
            source,
            selectedAudioCodec: "aac",
            range: VideoPlaybackRange.Sdr,
            profile: null,
            supportedVideoRangeTypes: null,
            directPlayAllowed: true,
            directStreamAllowed: true,
            transcodingAllowed: true);

        Assert.Equal(expected, decision.Method);
    }
}
