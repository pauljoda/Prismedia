import type { SubtitleSourceCode } from "$lib/api/generated/codes";

export const subtitleDisplayStyles = ["stylized", "classic", "outline"] as const;

export type SubtitleDisplayStyle = (typeof subtitleDisplayStyles)[number];

export interface SubtitleAppearance {
  style: SubtitleDisplayStyle;
  fontScale: number;
  positionPercent: number;
  opacity: number;
}

export const defaultSubtitleAppearance: SubtitleAppearance = {
  style: "stylized",
  fontScale: 1,
  positionPercent: 88,
  opacity: 1,
};

export type SubtitleSource = SubtitleSourceCode;

export type SubtitleSourceFormat = "vtt" | "srt" | "ass" | "ssa";

export interface VideoSubtitleTrack {
  id: string;
  videoId: string;
  language: string;
  label: string | null;
  format: "vtt";
  source: SubtitleSource;
  sourceFormat: SubtitleSourceFormat;
  isDefault: boolean;
  url: string;
  sourceUrl: string | null;
  createdAt: string;
}

export interface SubtitleCue {
  start: number;
  end: number;
  text: string;
}
