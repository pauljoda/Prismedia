import { describe, expect, it } from "vitest";
import type { EntityCapability } from "$lib/api/generated/model";
import { extractVideoPlayerProps, getPlaybackState } from "./video-capabilities";

describe("getPlaybackState", () => {
  it("maps the generated playback capability into resume state", () => {
    const capabilities: EntityCapability[] = [
      {
        kind: "playback",
        playCount: 3,
        skipCount: 1,
        playDurationSeconds: 120,
        resumeSeconds: 42,
        lastPlayedAt: "2026-05-15T10:00:00Z",
        completedAt: null,
      },
    ];

    expect(getPlaybackState(capabilities)).toEqual({
      playCount: 3,
      skipCount: 1,
      playDurationSeconds: 120,
      resumeSeconds: 42,
      lastPlayedAt: "2026-05-15T10:00:00Z",
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

    expect(getPlaybackState(capabilities)).toBeNull();
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
            path: "/Videos/video-1/Trickplay/280/tiles.m3u8",
            mimeType: "application/vnd.apple.mpegurl",
          },
        ],
      },
    ];

    expect(extractVideoPlayerProps("video-1", capabilities).trickplayPlaylist).toBe(
      "/Videos/video-1/Trickplay/280/tiles.m3u8",
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

  it("maps Jellyfin audio streams into player audio options", () => {
    const props = extractVideoPlayerProps("video-1", [], {
      PlaySessionId: "session-1",
      ErrorCode: null,
      MediaSources: [
        {
          Id: "source-1",
          Path: "/media/movie.mkv",
          Protocol: "File",
          Container: "mkv",
          Size: null,
          Name: "movie.mkv",
          RunTimeTicks: 600_000_000,
          SupportsDirectPlay: false,
          SupportsDirectStream: false,
          SupportsTranscoding: true,
          TranscodingUrl: "/Videos/video-1/master.m3u8?AudioStreamIndex=2",
          TranscodingSubProtocol: "hls",
          TranscodingContainer: "ts",
          MediaStreams: [
            {
              Index: 0,
              Type: "Video",
              Codec: "h264",
              DisplayTitle: "Video",
              IsDefault: true,
            },
            {
              Index: 1,
              Type: "Audio",
              Codec: "aac",
              Language: "spa",
              DisplayTitle: "Spanish",
              Channels: 2,
              IsDefault: false,
            },
            {
              Index: 2,
              Type: "Audio",
              Codec: "aac",
              Language: "eng",
              DisplayTitle: "English",
              Channels: 2,
              IsDefault: true,
            },
          ],
        },
      ],
    }, 2);

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
    const props = extractVideoPlayerProps("video-1", [], {
      PlaySessionId: "session-1",
      ErrorCode: null,
      MediaSources: [
        {
          Id: "source-1",
          Path: "/media/movie.mp4",
          Protocol: "File",
          Container: "mp4",
          Size: null,
          Name: "movie.mp4",
          RunTimeTicks: 600_000_000,
          SupportsDirectPlay: true,
          SupportsDirectStream: true,
          SupportsTranscoding: true,
          TranscodingUrl: null,
          TranscodingSubProtocol: null,
          TranscodingContainer: null,
          MediaStreams: [
            {
              Index: 0,
              Type: "Video",
              Codec: "h264",
              DisplayTitle: "Video",
              IsDefault: true,
            },
            {
              Index: 1,
              Type: "Audio",
              Codec: "aac",
              Language: "ita",
              DisplayTitle: "Italian",
              Channels: 2,
              IsDefault: true,
            },
            {
              Index: 2,
              Type: "Audio",
              Codec: "aac",
              Language: "eng",
              DisplayTitle: "English",
              Channels: 2,
              IsDefault: false,
            },
          ],
        },
      ],
    }, 2);

    expect(props.src).toBe("/Videos/video-1/master.m3u8?AudioStreamIndex=2");
    expect(props.audioTracks.find((track) => track.streamIndex === 2)?.selected).toBe(true);
    expect(props.qualityRungs[0]?.url).toContain("AudioStreamIndex=2");
  });

  it("does not synthesize HLS URLs when playback negotiation disables transcoding", () => {
    const props = extractVideoPlayerProps("video-1", [], {
      PlaySessionId: "session-1",
      ErrorCode: null,
      MediaSources: [
        {
          Id: "source-1",
          Path: "/media/new-import.mkv",
          Protocol: "File",
          Container: "mkv",
          Size: null,
          Name: "new-import.mkv",
          RunTimeTicks: null,
          SupportsDirectPlay: false,
          SupportsDirectStream: false,
          SupportsTranscoding: false,
          TranscodingUrl: null,
          TranscodingSubProtocol: null,
          TranscodingContainer: null,
          MediaStreams: [
            {
              Index: 0,
              Type: "Video",
              Codec: "h264",
              DisplayTitle: "Video",
              IsDefault: true,
            },
          ],
        },
      ],
    });

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
    ], {
      PlaySessionId: "session-1",
      ErrorCode: null,
      MediaSources: [
        {
          Id: "source-1",
          Path: "/media/movie.mp4",
          Protocol: "File",
          Container: "mp4",
          SupportsDirectPlay: false,
          SupportsDirectStream: false,
          SupportsTranscoding: true,
          TranscodingUrl: "/Videos/video-1/master.m3u8",
          TranscodingSubProtocol: "hls",
          TranscodingContainer: "ts",
          MediaStreams: [
            {
              Index: 0,
              Type: "Video",
              Codec: "hevc",
              Width: 3840,
              Height: 2160,
              VideoRange: "HDR",
              VideoRangeType: "HDR10",
              ColorTransfer: "smpte2084",
              ColorPrimaries: "bt2020",
              ColorSpace: "bt2020nc",
              IsDefault: true,
            },
          ],
        },
      ],
    });

    expect(props.directSrc).toBe("");
    expect(props.src).toBe("/Videos/video-1/master.m3u8");
    expect(props.colorPipelineLabel).toBe("HDR10 -> SDR tone map H.264");
    expect(props.resolutionLabel).toBe("4K");
    expect(props.dynamicRangeLabel).toBe("HDR10");
    expect(props.videoCodecLabel).toBe("HEVC");
    expect(props.streamMethod).toBe("transcode");
    expect(props.qualityRungs.length).toBeGreaterThan(0);
    expect(props.qualityRungs[0]).toMatchObject({
      name: "120mbps",
      url: "/Videos/video-1/hls/120mbps/stream.m3u8",
    });
  });
});
