export const playbackModes = ["direct", "hls"] as const;
export type PlaybackMode = (typeof playbackModes)[number];

export function normalizePlaybackMode(value: unknown): PlaybackMode {
  return typeof value === "string" && (playbackModes as readonly string[]).includes(value)
    ? (value as PlaybackMode)
    : "direct";
}

export const hlsTranscoderProfiles = [
  "Software",
  "Auto",
  "VideoToolbox",
  "Vaapi",
  "Nvenc",
  "Qsv",
] as const;
export type HlsTranscoderProfile = (typeof hlsTranscoderProfiles)[number];

export function normalizeHlsTranscoderProfile(value: unknown): HlsTranscoderProfile {
  return typeof value === "string" && (hlsTranscoderProfiles as readonly string[]).includes(value)
    ? (value as HlsTranscoderProfile)
    : "Software";
}

export const subtitleDisplayStyles = ["stylized", "classic", "outline"] as const;
export type SubtitleDisplayStyle = (typeof subtitleDisplayStyles)[number];

export interface SubtitleAppearance {
  style: SubtitleDisplayStyle;
  fontScale: number;
  positionPercent: number;
  /** Overall caption layer opacity (0.2–1.0). */
  opacity: number;
}

export const defaultSubtitleAppearance: SubtitleAppearance = {
  style: "stylized",
  fontScale: 1,
  positionPercent: 88,
  opacity: 1,
};

export interface StorageStatsDto {
  thumbnailsBytes: number;
  previewsBytes: number;
  trickplayBytes: number;
  totalBytes: number;
}
