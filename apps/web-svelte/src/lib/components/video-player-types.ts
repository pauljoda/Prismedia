export interface VideoPlayerHandle {
  seekTo: (time: number) => void;
  seekBy: (delta: number) => void;
  toggleMute: () => void;
  togglePlay: () => void;
}

export interface ActiveCue {
  start: number;
  end: number;
  text: string;
}

export interface VideoPlayerMarker {
  id: string;
  time: number;
  endTime?: number | null;
  title: string;
}

export interface VideoPlayerAudioTrack {
  id: string;
  streamIndex: number;
  label: string;
  selected: boolean;
  /** Short, viewer-friendly format descriptor (e.g. "Dolby Atmos 7.1") for the status badge. */
  formatLabel?: string | null;
}

export type PlaybackMode = "direct" | "hls";

// "direct" plays the original file, "auto" lets the server pick, and any other string is a quality-rung
// name (e.g. "8mbps") the viewer pinned. (string & {}) keeps editor autocomplete for the literals.
export type QualityMode = "direct" | "auto" | (string & {});

/** A manual quality tier the player can pin, resolved to a ready-to-load variant playlist URL. */
export interface PlayerQualityRung {
  /** Server rendition name (e.g. "8mbps"). */
  name: string;
  /** Viewer-facing label, e.g. "1080p · 8 Mbps". */
  label: string;
  /** Target video bitrate in bits per second, used for the persisted max-quality preference. */
  bitrate: number;
  /** Absolute variant-playlist URL that streams this tier. */
  url: string;
}

export type SettingsView = "root" | "quality" | "speed" | "audio" | "captions" | "subtitle-style";

export type HlsStatus = {
  state: "idle" | "pending" | "ready" | "error";
  error?: string | null;
};

export type CastWindow = Window &
  typeof globalThis & {
    chrome?: { cast?: { isAvailable?: boolean } };
    cast?: { framework?: unknown };
    __onGCastApiAvailable?: (isAvailable: boolean) => void;
  };

export interface QualityOption {
  value: QualityMode;
  label: string;
}

export interface AudioTrackOption {
  id: string;
  index: number;
  streamIndex: number | null;
  label: string;
  selected: boolean;
  source: "native" | "external";
}

export const PLAYBACK_RATES = [0.75, 1, 1.25, 1.5, 2];

export const HLS_RETRY_AFTER_SECONDS = 2;

export const MARKER_CHAPTERS_TRACK_ID = "prismedia-marker-chapters";

export const GOOGLE_CAST_SENDER_URL =
  "https://www.gstatic.com/cv/js/sender/v1/cast_sender.js?loadCastFramework=1";
