import type { LibrarySettings } from "$lib/api/prismedia";
import type {
  SubtitleAppearance,
  SubtitleDisplayStyle,
} from "$lib/player/subtitle-types";

const transcriptDockedKey = "prismedia:transcript-docked";
const transcriptDockWidthKey = "prismedia:transcript-dock-width";

export interface VideoSubtitleDefaults {
  autoEnable: boolean;
  preferredLanguages: string;
  appearance: SubtitleAppearance;
}

export interface TranscriptDockPreferences {
  docked: boolean;
  videoPercent: number;
}

export function formatVideoTimestamp(seconds: number): string {
  const safeSeconds = Number.isFinite(seconds) ? Math.max(0, seconds) : 0;
  const hours = Math.floor(safeSeconds / 3600);
  const minutes = Math.floor((safeSeconds % 3600) / 60);
  const wholeSeconds = Math.floor(safeSeconds % 60);
  if (hours > 0) {
    return `${hours}:${String(minutes).padStart(2, "0")}:${String(wholeSeconds).padStart(2, "0")}`;
  }
  return `${minutes}:${String(wholeSeconds).padStart(2, "0")}`;
}

export function buildSubtitleDefaults(
  settings: LibrarySettings | null,
): VideoSubtitleDefaults | undefined {
  if (!settings) return undefined;

  return {
    autoEnable: settings.subtitlesAutoEnable ?? false,
    preferredLanguages: settings.subtitlesPreferredLanguages ?? "en,eng",
    appearance: {
      style: (settings.subtitleStyle ?? "stylized") as SubtitleDisplayStyle,
      fontScale: settings.subtitleFontScale ?? 1,
      positionPercent: settings.subtitlePositionPercent ?? 88,
      opacity: settings.subtitleOpacity ?? 1,
    },
  };
}

export function clampTranscriptDockPercent(value: number): number {
  return Math.max(40, Math.min(92, value));
}

export function readTranscriptDockPreferences(storage: Pick<Storage, "getItem">): TranscriptDockPreferences {
  const savedWidth = Number(storage.getItem(transcriptDockWidthKey));
  return {
    docked: storage.getItem(transcriptDockedKey) === "1",
    videoPercent: Number.isFinite(savedWidth) && savedWidth >= 40 && savedWidth <= 92
      ? savedWidth
      : 80,
  };
}

export function writeTranscriptDockPreference(storage: Pick<Storage, "setItem">, docked: boolean): void {
  storage.setItem(transcriptDockedKey, docked ? "1" : "0");
}

export function writeTranscriptDockWidth(storage: Pick<Storage, "setItem">, videoPercent: number): void {
  storage.setItem(transcriptDockWidthKey, String(Math.round(clampTranscriptDockPercent(videoPercent))));
}
