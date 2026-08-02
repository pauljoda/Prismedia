import type {
  EntityCapability,
  VideoPlaybackPlanResponse,
  VideoPlaybackStream,
  VideoTranscodingInfo,
} from "$lib/api/generated/model";
import {
  getGetVideoPlaybackHlsAssetUrl,
  getGetVideoPlaybackSourceUrl,
} from "$lib/api/generated/prismedia";
import {
  STREAM_KIND,
  VIDEO_PLAYBACK_METHOD,
} from "$lib/api/generated/codes";
import type {
  VideoPlayerAudioTrack,
  VideoPlayerMarker,
} from "$lib/components/VideoPlayer.svelte";
import type { PlayerQualityRung } from "$lib/components/video-player-types";
import { qualityRungsForSource } from "$lib/player/quality-ladder";
import { getCapability } from "$lib/api/capabilities";
import { apiPath, assetUrl } from "$lib/api/orval-fetch";
import type {
  SubtitleSource,
  SubtitleSourceFormat,
  VideoSubtitleTrack,
} from "$lib/player/subtitle-types";
import { numberValue, positiveNumberValue } from "$lib/utils/format";
import {
  audioFormatBadge,
  dynamicRangeBadge,
  resolutionBadge,
  videoCodecBadge,
  type StreamMethod,
} from "$lib/player/media-badges";
import { CAPABILITY_KIND, ENTITY_FILE_ROLE } from "./entity-codes";

export interface VideoPlayerProps {
  src: string;
  directSrc: string;
  codec: string | null;
  sourceWidth: number | null;
  sourceHeight: number | null;
  poster: string;
  markers: VideoPlayerMarker[];
  duration: number;
  trickplayPlaylist: string;
  sessionId: string | null;
  subtitleTracks: VideoSubtitleTrack[];
  audioTracks: VideoPlayerAudioTrack[];
  colorPipelineLabel: string | null;
  /** Marketing resolution tier of the source ("4K", "1080p", …), or null when unknown. */
  resolutionLabel: string | null;
  /** Friendly HDR format of the source ("Dolby Vision", "HDR10", …), or null for SDR. */
  dynamicRangeLabel: string | null;
  /** Source video codec as viewers know it ("HEVC", "H.264", …). */
  videoCodecLabel: string | null;
  /** Default audio track's format descriptor ("Dolby Atmos 7.1", …) for the status badge. */
  audioFormatLabel: string | null;
  /** The server's negotiated delivery method, before any client-side fallback. */
  streamMethod: StreamMethod;
  /** Manual quality tiers the viewer can pin, each a ready-to-load variant URL. */
  qualityRungs: PlayerQualityRung[];
}

export function extractVideoPlayerProps(
  videoId: string,
  capabilities: EntityCapability[],
  playbackPlan: VideoPlaybackPlanResponse | null = null,
  selectedAudioStreamIndex: number | null = null,
): VideoPlayerProps {
  const technical = getCapability(capabilities, CAPABILITY_KIND.technical);
  const images = getCapability(capabilities, CAPABILITY_KIND.images);
  const files = getCapability(capabilities, CAPABILITY_KIND.files);
  const markers = getCapability(capabilities, CAPABILITY_KIND.markers);
  const subtitles = getCapability(capabilities, CAPABILITY_KIND.subtitles);

  const sourceFile = files?.items.find((f) => f.role === ENTITY_FILE_ROLE.source);
  const mediaSource = playbackPlan?.source ?? null;
  const videoStream = mediaSource?.streams.find((stream) => stream.type === STREAM_KIND.video);
  const audioStreams = (mediaSource?.streams ?? []).filter((stream) => stream.type === STREAM_KIND.audio);
  const defaultAudioStreamIndex =
    selectedAudioStreamIndex ??
    streamIndex(audioStreams.find((stream) => stream.isDefault)) ??
    streamIndex(audioStreams[0]) ??
    null;
  const trickplayFile = files?.items.find((f) => f.role === ENTITY_FILE_ROLE.trickplay);
  const trickplayImage = images?.items.find((asset) =>
    asset.kind === ENTITY_FILE_ROLE.trickplay &&
    asset.path.toLowerCase().endsWith(".m3u8")
  );
  const trickplayPath = trickplayFile?.path ?? trickplayImage?.path ?? "";
  const trickplayPlaylist = trickplayPath ? apiPath(trickplayPath) : "";
  const directPlayable = isBrowserNativeVideoSource(sourceFile?.path, technical?.container);
  const directSrc = mediaSource?.method === VIDEO_PLAYBACK_METHOD.direct
    ? apiPath(mediaSource.url)
    : !mediaSource && directPlayable
      ? apiPath(getGetVideoPlaybackSourceUrl(videoId))
      : "";
  const hlsSrc = mediaSource?.supportsTranscoding === false
    ? ""
    : mediaSource && mediaSource.method !== VIDEO_PLAYBACK_METHOD.direct
      ? apiPath(mediaSource.url)
      : apiPath(getGetVideoPlaybackHlsAssetUrl(videoId, "master.m3u8", {
          ...(defaultAudioStreamIndex == null ? {} : { audioStreamIndex: defaultAudioStreamIndex }),
        }));
  const defaultAudioStream =
    audioStreams.find((stream) => streamIndex(stream) === defaultAudioStreamIndex) ?? audioStreams[0] ?? null;

  return {
    src: hlsSrc,
    directSrc,
    codec: videoStream?.codec ?? technical?.codec ?? null,
    sourceWidth: positiveNumberValue(videoStream?.width) ?? positiveNumberValue(technical?.width),
    sourceHeight: positiveNumberValue(videoStream?.height) ?? positiveNumberValue(technical?.height),
    poster: assetUrl(images?.thumbnailUrl) || "",
    markers: (markers?.items ?? []).map((m) => ({
      id: m.id,
      time: Number(m.seconds),
      endTime: m.endSeconds == null ? null : Number(m.endSeconds),
      title: m.title,
    })),
    duration: positiveNumberValue(mediaSource?.durationSeconds) ?? parseDotnetTimeSpan(technical?.duration),
    trickplayPlaylist,
    sessionId: playbackPlan?.sessionId ?? null,
    colorPipelineLabel: colorPipelineLabel(
      videoStream,
      mediaSource?.transcoding ?? null,
      mediaSource?.method,
    ),
    resolutionLabel: resolutionBadge(
      videoStream?.width ?? positiveNumberValue(technical?.width),
      videoStream?.height ?? positiveNumberValue(technical?.height),
    ),
    dynamicRangeLabel: dynamicRangeBadge(videoStream),
    videoCodecLabel: videoCodecBadge(videoStream?.codec ?? technical?.codec),
    audioFormatLabel: audioFormatBadge(defaultAudioStream),
    streamMethod: resolveStreamMethod(mediaSource),
    qualityRungs: buildQualityRungs(
      videoId,
      positiveNumberValue(videoStream?.bitRate) ?? positiveNumberValue(technical?.bitRate),
      positiveNumberValue(videoStream?.height) ?? positiveNumberValue(technical?.height),
      videoStream?.codec ?? technical?.codec,
      defaultAudioStreamIndex,
      mediaSource?.supportsTranscoding,
    ),
    audioTracks: audioStreams.map((stream) => ({
      id: `audio-${streamIndex(stream) ?? 0}`,
      streamIndex: streamIndex(stream) ?? 0,
      label: audioStreamLabel(stream),
      formatLabel: audioFormatBadge(stream),
      selected: defaultAudioStreamIndex === streamIndex(stream),
    })),
    subtitleTracks: (subtitles?.items ?? []).map((s) =>
      mapEntitySubtitle(videoId, { ...s, source: String(s.source) }),
    ),
  };
}

// Reads the server's negotiated delivery decision. The player may still fall back at runtime, so
// this is only the starting plan.
function resolveStreamMethod(
  mediaSource: VideoPlaybackPlanResponse["source"] | null | undefined,
): StreamMethod {
  return mediaSource?.method ?? VIDEO_PLAYBACK_METHOD.transcode;
}

// Builds the manual quality tiers for the player. Each tier points at the variant playlist the server
// already produces, carrying the active audio so a quality switch
// keeps the chosen track. Skipped only when the source explicitly cannot be transcoded.
function buildQualityRungs(
  videoId: string,
  sourceBitrate: number | null,
  sourceHeight: number | null,
  codec: string | null | undefined,
  audioStreamIndex: number | null,
  supportsTranscoding: boolean | null | undefined,
): PlayerQualityRung[] {
  if (supportsTranscoding === false) return [];
  return qualityRungsForSource(sourceBitrate, sourceHeight, codec).map((rung) => ({
    name: rung.name,
    label: rung.label,
    bitrate: rung.bitrate,
    url: apiPath(getGetVideoPlaybackHlsAssetUrl(
      videoId,
      `v/${rung.name}/stream.m3u8`,
      audioStreamIndex == null ? undefined : { audioStreamIndex },
    )),
  }));
}

function colorPipelineLabel(
  videoStream: VideoPlaybackStream | null | undefined,
  transcodingInfo: VideoTranscodingInfo | null | undefined,
  method: StreamMethod | undefined,
): string | null {
  const sourceRange = sourceRangeLabel(videoStream);
  if (method === VIDEO_PLAYBACK_METHOD.direct || transcodingInfo?.isVideoDirect) {
    return `${sourceRange} direct`;
  }

  const outputCodec = codecLabel(transcodingInfo?.videoCodec);
  if (sourceRange === "SDR") {
    return `SDR -> ${outputCodec} SDR`;
  }

  return `${sourceRange} -> SDR tone map ${outputCodec}`;
}

function codecLabel(codec: string | null | undefined): string {
  if (!codec) return "H.264";
  const normalized = codec.toLowerCase();
  if (normalized === "h264" || normalized === "avc") return "H.264";
  if (normalized === "h265" || normalized === "hevc") return "HEVC";
  return codec.toUpperCase();
}

function sourceRangeLabel(videoStream: VideoPlaybackStream | null | undefined): string {
  const type = videoStream?.videoRangeType?.trim();
  if (!type || type.toUpperCase() === "SDR") return "SDR";
  if (type.toUpperCase() === "DOVI") {
    return videoStream?.dvProfile ? `DOVI P${videoStream.dvProfile}` : "DOVI";
  }
  return type;
}

function audioStreamLabel(stream: VideoPlaybackStream): string {
  const index = streamIndex(stream) ?? 0;
  const title = stream.displayTitle?.trim();
  const language = languageLabel(stream.language);
  const codec = stream.codec ? stream.codec.toUpperCase() : null;
  const channels = positiveNumberValue(stream.channels);
  const parts = [title || language || `Track ${index}`, codec, channels ? `${channels}ch` : null]
    .filter(Boolean);
  return `${parts.join(" · ")}${stream.isDefault ? " · Default" : ""}`;
}

function streamIndex(stream: VideoPlaybackStream | null | undefined): number | null {
  return numberValue(stream?.index);
}

function languageLabel(language: string | null | undefined): string | null {
  if (!language || language === "und") return null;
  try {
    return new Intl.DisplayNames(undefined, { type: "language" }).of(language) ?? language.toUpperCase();
  } catch {
    return language.toUpperCase();
  }
}

function mapEntitySubtitle(
  videoId: string,
  sub: {
    id: string;
    language: string;
    label: string | null;
    format: string;
    source: string;
    storagePath: string;
    sourceFormat: string;
    sourcePath: string | null;
    isDefault: boolean;
  },
): VideoSubtitleTrack {
  const sourceFormat = parseSubtitleSourceFormat(sub.sourceFormat);
  const hasStyledSource =
    (sourceFormat === "ass" || sourceFormat === "ssa") && Boolean(sub.sourcePath);
  const contentRevision = subtitleContentRevision(sub.storagePath);

  return {
    id: sub.id,
    videoId,
    language: sub.language,
    label: sub.label,
    format: "vtt",
    source: parseSubtitleSource(sub.source),
    sourceFormat,
    isDefault: sub.isDefault,
    url: subtitleAssetUrl(`/videos/${videoId}/subtitles/${sub.id}`, contentRevision),
    sourceUrl: hasStyledSource
      ? subtitleAssetUrl(`/videos/${videoId}/subtitles/${sub.id}/source`, contentRevision)
      : null,
    createdAt: "",
  };
}

function subtitleContentRevision(storagePath: string): string | null {
  const fileName = storagePath.split(/[\\/]/).at(-1) ?? "";
  const match = fileName.match(/^sidecar-[a-f0-9]{32}-([a-f0-9]{32})\.vtt$/i);
  return match?.[1]?.toLowerCase() ?? null;
}

function subtitleAssetUrl(path: string, contentRevision: string | null): string {
  const url = apiPath(path);
  return contentRevision ? `${url}?v=${encodeURIComponent(contentRevision)}` : url;
}

function parseSubtitleSource(value: string): SubtitleSource {
  switch (value) {
    case "manual":
    case "embedded":
    case "generated":
    case "provider":
    case "upload":
    case "sidecar":
      return value;
    default:
      return "manual";
  }
}

function parseSubtitleSourceFormat(
  value: string | null | undefined,
): SubtitleSourceFormat {
  switch (value) {
    case "srt":
    case "ass":
    case "ssa":
    case "vtt":
      return value;
    default:
      return "vtt";
  }
}


function isBrowserNativeVideoSource(
  path: string | null | undefined,
  container: string | null | undefined,
): boolean {
  const normalizedContainer = container?.trim().toLowerCase();
  if (normalizedContainer && ["matroska", "mkv", "avi", "wmv", "flv", "mpegts"].includes(normalizedContainer)) {
    return false;
  }

  const extension = path?.match(/\.([a-z0-9]+)$/i)?.[1]?.toLowerCase();
  if (!extension) return false;
  return ["mp4", "m4v", "webm", "ogg", "ogv"].includes(extension);
}

function parseDotnetTimeSpan(value: string | null | undefined): number {
  if (!value) return 0;
  const match = value.match(
    /^-?(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?$/,
  );
  if (!match) return 0;
  const days = match[1] ? parseInt(match[1], 10) : 0;
  const hours = parseInt(match[2], 10);
  const minutes = parseInt(match[3], 10);
  const seconds = parseInt(match[4], 10);
  const frac = match[5] ? parseFloat(`0.${match[5]}`) : 0;
  return days * 86400 + hours * 3600 + minutes * 60 + seconds + frac;
}

export interface ConsumptionState {
  accessCount: number;
  completionCount: number;
  skipCount: number;
  activeSeconds: number;
  resumeSeconds: number;
  lastAccessedAt: string | null;
  lastActiveAt: string | null;
  completedAt: string | null;
}

export function getConsumptionState(
  capabilities: EntityCapability[],
): ConsumptionState | null {
  const cap = getCapability(capabilities, CAPABILITY_KIND.consumption);
  if (!cap) return null;
  return {
    accessCount: positiveNumberValue(cap.accessCount) ?? 0,
    completionCount: positiveNumberValue(cap.completionCount) ?? 0,
    skipCount: positiveNumberValue(cap.skipCount) ?? 0,
    activeSeconds: positiveNumberValue(cap.activeSeconds) ?? 0,
    resumeSeconds: positiveNumberValue(cap.resumeSeconds) ?? 0,
    lastAccessedAt: cap.lastAccessedAt,
    lastActiveAt: cap.lastActiveAt,
    completedAt: cap.completedAt,
  };
}
