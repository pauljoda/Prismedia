import { PLAYBACK_MODE, type PlaybackModeCode } from "$lib/api/generated/codes";

export {
  defaultSubtitleAppearance,
  subtitleDisplayStyles,
  type SubtitleAppearance,
  type SubtitleDisplayStyle,
} from "$lib/player/subtitle-types";

export const BACKGROUND_WORKER_CONCURRENCY_MIN = 1;
export const BACKGROUND_WORKER_CONCURRENCY_MAX = 32;

export const playbackModes = Object.values(PLAYBACK_MODE) as readonly PlaybackModeCode[];
export type PlaybackMode = PlaybackModeCode;

export function normalizePlaybackMode(value: unknown): PlaybackMode {
  return typeof value === "string" && (playbackModes as readonly string[]).includes(value)
    ? (value as PlaybackMode)
    : PLAYBACK_MODE.direct;
}

export const hlsTranscoderProfiles = [
  "Auto",
  "Software",
  "VideoToolbox",
  "Vaapi",
  "Nvenc",
  "Qsv",
] as const;

export type HlsTranscoderProfile = (typeof hlsTranscoderProfiles)[number];

export function normalizeHlsTranscoderProfile(value: unknown): HlsTranscoderProfile {
  return typeof value === "string" && (hlsTranscoderProfiles as readonly string[]).includes(value)
    ? (value as HlsTranscoderProfile)
    : "Auto";
}
