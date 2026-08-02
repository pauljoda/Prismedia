import { describe, expect, it } from "vitest";
import type {
  EntityCapability,
  VideoPlaybackPlanResponse,
  VideoPlaybackSource,
  VideoPlaybackStream,
} from "$lib/api/generated/model";
import { CAPABILITY_KIND, STREAM_KIND, VIDEO_PLAYBACK_METHOD } from "$lib/api/generated/codes";
import { extractVideoPlayerProps, getConsumptionState } from "./video-capabilities";

describe("getConsumptionState", () => {
  it("maps the generated playback capability into resume state", () => {
    const capabilities: EntityCapability[] = [
      {
        kind: CAPABILITY_KIND.consumption,
        accessCount: 3,
        completionCount: 2,
        skipCount: 1,
        activeSeconds: 120,
        resumeSeconds: 42,
        lastAccessedAt: "2026-05-15T09:00:00Z",
        lastActiveAt: "2026-05-15T10:00:00Z",
        completedAt: null,
      },
    ];

    expect(getConsumptionState(capabilities)).toEqual({
      accessCount: 3,
      completionCount: 2,
      skipCount: 1,
      activeSeconds: 120,
      resumeSeconds: 42,
      lastAccessedAt: "2026-05-15T09:00:00Z",
      lastActiveAt: "2026-05-15T10:00:00Z",
      completedAt: null,
    });
  });

  it("returns null when the entity has no playback capability", () => {
    const capabilities: EntityCapability[] = [
      {
        kind: "progress",
        currentEntityId: "chapter-1",
        unit: "page",
        index: 8,
        total: 24,
        mode: "paged",
        completedAt: null,
        updatedAt: null,
      },
    ];

    expect(getConsumptionState(capabilities)).toBeNull();
  });
});

describe("extractVideoPlayerProps", () => {
  it("does not expose direct playback for Matroska source files", () => {
    const capabilities: EntityCapability[] = [
      {
        kind: "files",
        items: [
          {
            role: "source",
            path: "/media/show/episode.mkv",
            mimeType: null,
          },
        ],
      },
      {
        kind: "technical",
        duration: "00:01:00",
        width: 1920,
        height: 1080,
        frameRate: 24,
        bitRate: null,
        sampleRate: null,
        channels: null,
        codec: "h264",
        container: "matroska",
        format: null,
      },
    ];

    const props = extractVideoPlayerProps("video-1", capabilities);

    expect(props.directSrc).toBe("");
    expect(props.sourceWidth).toBe(1920);
    expect(props.sourceHeight).toBe(1080);
  });

  it("uses subtitle endpoints instead of raw storage paths", () => {
    const capabilities: EntityCapability[] = [
      {
        kind: "subtitles",
        items: [
          {
            id: "track-1",
            language: "eng",
            label: "SDH",
            format: "vtt",
            source: "embedded",
            storagePath: "/tmp/cache/videos/video-1/subtitles/track.vtt",
            sourceFormat: "vtt",
            sourcePath: null,
            isDefault: false,
          },
        ],
      },
    ];

    expect(extractVideoPlayerProps("video-1", capabilities).subtitleTracks[0]).toMatchObject({
      url: "/api/videos/video-1/subtitles/track-1",
      sourceUrl: null,
    });
  });

  it("revises subtitle endpoint identity when sidecar content changes", () => {
    const sourceToken = "a".repeat(32);
    const firstContentToken = "b".repeat(32);
    const secondContentToken = "c".repeat(32);
    const subtitleCapability = (contentToken: string): EntityCapability => ({
      kind: "subtitles",
      items: [
        {
          id: "track-1",
          language: "eng",
          label: null,
          format: "vtt",
          source: "sidecar",
          storagePath: `/tmp/cache/videos/video-1/subtitles/sidecar-${sourceToken}-${contentToken}.vtt`,
          sourceFormat: "vtt",
          sourcePath: null,
          isDefault: false,
        },
      ],
    });

    const firstUrl = extractVideoPlayerProps("video-1", [
      subtitleCapability(firstContentToken),
    ]).subtitleTracks[0]?.url;
    const secondUrl = extractVideoPlayerProps("video-1", [
      subtitleCapability(secondContentToken),
    ]).subtitleTracks[0]?.url;

    expect(firstUrl).toBe(`/api/videos/video-1/subtitles/track-1?v=${firstContentToken}`);
    expect(secondUrl).toBe(`/api/videos/video-1/subtitles/track-1?v=${secondContentToken}`);
    expect(secondUrl).not.toBe(firstUrl);
  });

  it("uses the advertised trickplay image playlist instead of guessing a fixed width", () => {
    const capabilities: EntityCapability[] = [
      {
        kind: "images",
        supportedKinds: ["thumbnail", "trickplay"],
        thumbnailUrl: null,
        thumbnail2xUrl: null,
        coverUrl: null,
        items: [
          {
            kind: "trickplay",
            path: "/api/playback/videos/video-1/trickplay/280/tiles.m3u8",
            mimeType: "application/vnd.apple.mpegurl",
          },
        ],
      },
    ];

    expect(extractVideoPlayerProps("video-1", capabilities).trickplayPlaylist).toBe(
      "/api/playback/videos/video-1/trickplay/280/tiles.m3u8",
    );
  });

  it("does not request trickplay until the backend advertises an asset", () => {
    expect(extractVideoPlayerProps("video-1", []).trickplayPlaylist).toBe("");
  });

  it("maps marker end times into player chapter markers", () => {
    const capabilities: EntityCapability[] = [
      {
        kind: "markers",
        items: [
          {
            id: "marker-1",
            title: "Intro",
            seconds: 8,
            endSeconds: 42,
          },
        ],
      },
    ];

    expect(extractVideoPlayerProps("video-1", capabilities).markers).toEqual([
      {
        id: "marker-1",
        time: 8,
        endTime: 42,
        title: "Intro",
      },
    ]);
  });

  it("maps native playback streams into player audio options", () => {
    const props = extractVideoPlayerProps("video-1", [], playbackPlan({
      streams: [
        playbackStream({ index: 0, type: STREAM_KIND.video, codec: "h264", displayTitle: "Video", isDefault: true }),
        playbackStream({ index: 1, type: STREAM_KIND.audio, codec: "aac", language: "spa", displayTitle: "Spanish", channels: 2 }),
        playbackStream({ index: 2, type: STREAM_KIND.audio, codec: "aac", language: "eng", displayTitle: "English", channels: 2, isDefault: true }),
      ],
    }), 2);

    expect(props.audioTracks).toEqual([
      expect.objectContaining({
        streamIndex: 1,
        label: "Spanish · AAC · 2ch",
        formatLabel: "AAC Stereo",
        selected: false,
      }),
      expect.objectContaining({
        streamIndex: 2,
        label: "English · AAC · 2ch · Default",
        formatLabel: "AAC Stereo",
        selected: true,
      }),
    ]);
    expect(props.audioFormatLabel).toBe("AAC Stereo");
  });

  it("carries the selected audio stream into fallback HLS URLs for direct-play sources", () => {
    const props = extractVideoPlayerProps("video-1", [], playbackPlan({
      container: "mp4",
      method: VIDEO_PLAYBACK_METHOD.direct,
      url: "/api/playback/videos/video-1/stream",
      transcoding: null,
      streams: [
        playbackStream({ index: 0, type: STREAM_KIND.video, codec: "h264", displayTitle: "Video", isDefault: true }),
        playbackStream({ index: 1, type: STREAM_KIND.audio, codec: "aac", language: "ita", displayTitle: "Italian", channels: 2, isDefault: true }),
        playbackStream({ index: 2, type: STREAM_KIND.audio, codec: "aac", language: "eng", displayTitle: "English", channels: 2 }),
      ],
    }), 2);

    expect(props.src).toBe("/api/playback/videos/video-1/hls/master.m3u8?audioStreamIndex=2");
    expect(props.directSrc).toBe("/api/playback/videos/video-1/stream");
    expect(props.audioTracks.find((track) => track.streamIndex === 2)?.selected).toBe(true);
    expect(props.qualityRungs[0]?.url).toContain("audioStreamIndex=2");
  });

  it("does not synthesize HLS URLs when playback negotiation disables transcoding", () => {
    const props = extractVideoPlayerProps("video-1", [], playbackPlan({
      supportsTranscoding: false,
      url: "",
      streams: [
        playbackStream({ index: 0, type: STREAM_KIND.video, codec: "h264", displayTitle: "Video", isDefault: true }),
      ],
    }));

    expect(props.src).toBe("");
    expect(props.directSrc).toBe("");
    expect(props.qualityRungs).toEqual([]);
  });

  it("trusts playback negotiation when HDR sources must transcode", () => {
    const props = extractVideoPlayerProps("video-1", [
      {
        kind: "files",
        items: [
          {
            role: "source",
            path: "/media/movie.mp4",
            mimeType: "video/mp4",
          },
        ],
      },
      {
        kind: "technical",
        duration: "00:01:00",
        width: 3840,
        height: 2160,
        frameRate: 24,
        bitRate: null,
        sampleRate: null,
        channels: null,
        codec: "hevc",
        container: "mp4",
        format: null,
      },
    ], playbackPlan({
      container: "mp4",
      streams: [
        playbackStream({
          index: 0,
          type: STREAM_KIND.video,
          codec: "hevc",
          width: 3840,
          height: 2160,
          videoRange: "HDR",
          videoRangeType: "HDR10",
          colorTransfer: "smpte2084",
          colorPrimaries: "bt2020",
          colorSpace: "bt2020nc",
          isDefault: true,
        }),
      ],
    }));

    expect(props.directSrc).toBe("");
    expect(props.src).toBe("/api/playback/videos/video-1/hls/master.m3u8");
    expect(props.colorPipelineLabel).toBe("HDR10 -> SDR tone map H.264");
    expect(props.resolutionLabel).toBe("4K");
    expect(props.dynamicRangeLabel).toBe("HDR10");
    expect(props.videoCodecLabel).toBe("HEVC");
    expect(props.streamMethod).toBe("transcode");
    expect(props.qualityRungs.length).toBeGreaterThan(0);
    expect(props.qualityRungs[0]).toMatchObject({
      name: "120mbps",
      url: "/api/playback/videos/video-1/hls/v/120mbps/stream.m3u8",
    });
  });
});

function playbackPlan(overrides: Partial<VideoPlaybackSource> = {}): VideoPlaybackPlanResponse {
  return {
    sessionId: "session-1",
    source: {
      id: "source-1",
      container: "mkv",
      durationSeconds: 60,
      method: VIDEO_PLAYBACK_METHOD.transcode,
      url: "/api/playback/videos/video-1/hls/master.m3u8",
      supportsTranscoding: true,
      streams: [],
      transcoding: {
        container: "ts",
        videoCodec: "h264",
        audioCodec: "aac",
        isVideoDirect: false,
        isAudioDirect: false,
      },
      ...overrides,
    },
  };
}

function playbackStream(overrides: Partial<VideoPlaybackStream>): VideoPlaybackStream {
  return {
    index: 0,
    type: STREAM_KIND.video,
    codec: null,
    language: null,
    displayTitle: null,
    width: null,
    height: null,
    averageFrameRate: null,
    bitRate: null,
    sampleRate: null,
    channels: null,
    ...overrides,
  };
}
