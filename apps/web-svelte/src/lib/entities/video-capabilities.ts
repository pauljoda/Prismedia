import type { EntityCapability } from "$lib/api/generated/model";
import type { JellyfinPlaybackInfoResponse } from "$lib/api/playback";
import type {
  VideoPlayerAudioTrack,
  VideoPlayerMarker,
} from "$lib/components/VideoPlayer.svelte";
import type { PlayerQualityRung } from "$lib/components/video-player-types";
import { qualityRungsForSource } from "$lib/player/quality-ladder";
import { getCapability } from "$lib/api/capabilities";
import { jellyfinApiPath, apiPath, assetUrl } from "$lib/api/orval-fetch";
import type {
  SubtitleSource,
  SubtitleSourceFormat,
  VideoSubtitleTrack,
} from "$lib/player/subtitle-types";
import { positiveNumberValue } from "$lib/utils/format";
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
  playSessionId: string | null;
  mediaSourceId: string | null;
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
  /** Manual quality tiers the viewer can pin (Jellyfin-style), each a ready-to-load variant URL. */
  qualityRungs: PlayerQualityRung[];
}

export function extractVideoPlayerProps(
  videoId: string,
  capabilities: EntityCapability[],
  playbackInfo: JellyfinPlaybackInfoResponse | null = null,
  selectedAudioStreamIndex: number | null = null,
): VideoPlayerProps {
  const technical = getCapability(capabilities, CAPABILITY_KIND.technical);
  const images = getCapability(capabilities, CAPABILITY_KIND.images);
  const files = getCapability(capabilities, CAPABILITY_KIND.files);
  const markers = getCapability(capabilities, CAPABILITY_KIND.markers);
  const subtitles = getCapability(capabilities, CAPABILITY_KIND.subtitles);

  const sourceFile = files?.items.find((f) => f.role === ENTITY_FILE_ROLE.source);
  const mediaSource = playbackInfo?.MediaSources?.[0] ?? null;
  const videoStream = mediaSource?.MediaStreams?.find((stream) => stream.Type === "Video");
  const audioStreams = (mediaSource?.MediaStreams ?? []).filter((stream) => stream.Type === "Audio");
  const defaultAudioStreamIndex =
    selectedAudioStreamIndex ??
    audioStreams.find((stream) => stream.IsDefault)?.Index ??
    audioStreams[0]?.Index ??
    null;
  const trickplayFile = files?.items.find((f) => f.role === ENTITY_FILE_ROLE.trickplay);
  const trickplayImage = images?.items.find((asset) =>
    asset.kind === ENTITY_FILE_ROLE.trickplay &&
    asset.path.toLowerCase().endsWith(".m3u8")
  );
  const trickplayPath = trickplayFile?.path ?? trickplayImage?.path ?? "";
  const trickplayPlaylist = trickplayPath ? jellyfinApiPath(trickplayPath) : "";
  const directPlayable = isBrowserNativeVideoSource(sourceFile?.path, technical?.container);
  const directSrc = (mediaSource?.SupportsDirectPlay ?? directPlayable)
    ? jellyfinApiPath(`/Videos/${videoId}/stream${mediaSource?.Id ? `?MediaSourceId=${mediaSource.Id}` : ""}`)
    : "";
  const hlsSrc = mediaSource?.TranscodingUrl
    ? jellyfinApiPath(mediaSource.TranscodingUrl)
    : jellyfinApiPath(appendAudioStreamIndex(`/Videos/${videoId}/master.m3u8`, defaultAudioStreamIndex));
  const defaultAudioStream =
    audioStreams.find((stream) => stream.Index === defaultAudioStreamIndex) ?? audioStreams[0] ?? null;

  return {
    src: hlsSrc,
    directSrc,
    codec: videoStream?.Codec ?? technical?.codec ?? null,
    sourceWidth: videoStream?.Width ?? positiveNumberValue(technical?.width),
    sourceHeight: videoStream?.Height ?? positiveNumberValue(technical?.height),
    poster: assetUrl(images?.thumbnailUrl) || "",
    markers: (markers?.items ?? []).map((m) => ({
      id: m.id,
      time: Number(m.seconds),
      endTime: m.endSeconds == null ? null : Number(m.endSeconds),
      title: m.title,
    })),
    duration: ticksToSeconds(mediaSource?.RunTimeTicks) || parseDotnetTimeSpan(technical?.duration),
    trickplayPlaylist,
    playSessionId: playbackInfo?.PlaySessionId ?? null,
    mediaSourceId: mediaSource?.Id ?? null,
    colorPipelineLabel: colorPipelineLabel(videoStream, mediaSource?.TranscodingInfo ?? null),
    resolutionLabel: resolutionBadge(
      videoStream?.Width ?? positiveNumberValue(technical?.width),
      videoStream?.Height ?? positiveNumberValue(technical?.height),
    ),
    dynamicRangeLabel: dynamicRangeBadge(videoStream),
    videoCodecLabel: videoCodecBadge(videoStream?.Codec ?? technical?.codec),
    audioFormatLabel: audioFormatBadge(defaultAudioStream),
    streamMethod: resolveStreamMethod(mediaSource),
    qualityRungs: buildQualityRungs(
      videoId,
      videoStream?.BitRate ?? positiveNumberValue(technical?.bitRate),
      videoStream?.Height ?? positiveNumberValue(technical?.height),
      videoStream?.Codec ?? technical?.codec,
      defaultAudioStreamIndex,
      mediaSource?.SupportsTranscoding,
    ),
    audioTracks: audioStreams.map((stream) => ({
      id: `audio-${stream.Index}`,
      streamIndex: stream.Index,
      label: audioStreamLabel(stream),
      formatLabel: audioFormatBadge(stream),
      selected: defaultAudioStreamIndex === stream.Index,
    })),
    subtitleTracks: (subtitles?.items ?? []).map((s) =>
      mapEntitySubtitle(videoId, { ...s, source: String(s.source) }),
    ),
  };
}

// Reads the server's negotiated delivery decision. SupportsDirectPlay means the raw file plays as-is;
// otherwise the TranscodingInfo says whether the video is copied (remux) or re-encoded. The player may
// still fall back at runtime, so this is only the starting plan.
function resolveStreamMethod(
  mediaSource: {
    SupportsDirectPlay?: boolean | null;
    TranscodingInfo?: { IsVideoDirect?: boolean | null } | null;
  } | null | undefined,
): StreamMethod {
  if (!mediaSource) return "transcode";
  if (mediaSource.SupportsDirectPlay) return "direct";
  if (mediaSource.TranscodingInfo?.IsVideoDirect) return "remux";
  return "transcode";
}

// Builds the manual quality tiers for the player. Each tier points at the variant playlist the server
// already produces (/Videos/{id}/hls/{name}/stream.m3u8), carrying the active audio so a quality switch
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
    url: jellyfinApiPath(
      appendAudioStreamIndex(`/Videos/${videoId}/hls/${rung.name}/stream.m3u8`, audioStreamIndex),
    ),
  }));
}

function appendAudioStreamIndex(path: string, audioStreamIndex: number | null): string {
  if (audioStreamIndex == null || /(?:[?&])AudioStreamIndex=/.test(path)) return path;
  const separator = path.includes("?") ? "&" : "?";
  return `${path}${separator}AudioStreamIndex=${audioStreamIndex}`;
}

function colorPipelineLabel(
  videoStream: {
    VideoRange?: string | null;
    VideoRangeType?: string | null;
    DvProfile?: number | null;
  } | null | undefined,
  transcodingInfo: {
    VideoCodec?: string | null;
    IsVideoDirect?: boolean | null;
  } | null | undefined,
): string | null {
  const sourceRange = sourceRangeLabel(videoStream);
  if (transcodingInfo?.IsVideoDirect) {
    return `${sourceRange} direct`;
  }

  const outputCodec = codecLabel(transcodingInfo?.VideoCodec);
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

function sourceRangeLabel(
  videoStream: {
    VideoRange?: string | null;
    VideoRangeType?: string | null;
    DvProfile?: number | null;
  } | null | undefined,
): string {
  const type = videoStream?.VideoRangeType?.trim();
  if (!type || type.toUpperCase() === "SDR") return "SDR";
  if (type.toUpperCase() === "DOVI") {
    return videoStream?.DvProfile ? `DOVI P${videoStream.DvProfile}` : "DOVI";
  }
  return type;
}

function audioStreamLabel(stream: {
  Index: number;
  Language?: string | null;
  DisplayTitle?: string | null;
  Channels?: number | null;
  Codec?: string | null;
  IsDefault?: boolean | null;
}): string {
  const title = stream.DisplayTitle?.trim();
  const language = languageLabel(stream.Language);
  const codec = stream.Codec ? stream.Codec.toUpperCase() : null;
  const channels = stream.Channels ? `${stream.Channels}ch` : null;
  const parts = [title || language || `Track ${stream.Index}`, codec, channels]
    .filter(Boolean);
  return `${parts.join(" · ")}${stream.IsDefault ? " · Default" : ""}`;
}

function languageLabel(language: string | null | undefined): string | null {
  if (!language || language === "und") return null;
  try {
    return new Intl.DisplayNames(undefined, { type: "language" }).of(language) ?? language.toUpperCase();
  } catch {
    return language.toUpperCase();
  }
}

function ticksToSeconds(value: number | null | undefined): number {
  return typeof value === "number" && Number.isFinite(value) && value > 0
    ? value / 10_000_000
    : 0;
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

  return {
    id: sub.id,
    videoId,
    language: sub.language,
    label: sub.label,
    format: "vtt",
    source: parseSubtitleSource(sub.source),
    sourceFormat,
    isDefault: sub.isDefault,
    url: apiPath(`/videos/${videoId}/subtitles/${sub.id}`),
    sourceUrl: hasStyledSource
      ? apiPath(`/videos/${videoId}/subtitles/${sub.id}/source`)
      : null,
    createdAt: "",
  };
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

export interface PlaybackState {
  playCount: number;
  playDurationSeconds: number;
  resumeSeconds: number;
  lastPlayedAt: string | null;
  completedAt: string | null;
}

export function getPlaybackState(
  capabilities: EntityCapability[],
): PlaybackState | null {
  const cap = getCapability(capabilities, CAPABILITY_KIND.playback);
  if (!cap) return null;
  return {
    playCount: positiveNumberValue(cap.playCount) ?? 0,
    playDurationSeconds: positiveNumberValue(cap.playDurationSeconds) ?? 0,
    resumeSeconds: positiveNumberValue(cap.resumeSeconds) ?? 0,
    lastPlayedAt: cap.lastPlayedAt,
    completedAt: cap.completedAt,
  };
}
