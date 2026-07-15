export type SubtitleSource =
  | "manual"
  | "embedded"
  | "generated"
  | "provider"
  | "upload"
  | "sidecar";

/**
 * The original on-disk format. `vtt` means the server only has a WebVTT
 * representation. `ass`/`ssa` means the original Advanced SubStation file is
 * preserved alongside the VTT fallback, and the player can render it with
 * full libass fidelity (fonts, positioning, colors, karaoke, animations) via
 * the `sourceUrl`.
 */
export type SubtitleSourceFormat = "vtt" | "srt" | "ass" | "ssa";

export interface VideoSubtitleTrackDto {
  id: string;
  videoId: string;
  language: string;
  label: string | null;
  format: "vtt";
  source: SubtitleSource;
  sourceFormat: SubtitleSourceFormat;
  isDefault: boolean;
  url: string;
  /** Present when the server has preserved the original file (e.g. .ass). */
  sourceUrl: string | null;
  createdAt: string;
}

export interface SubtitleCueDto {
  start: number;
  end: number;
  text: string;
}
