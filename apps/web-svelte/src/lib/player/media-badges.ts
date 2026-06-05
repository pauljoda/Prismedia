/**
 * Pure helpers that turn raw media-stream metadata into the short, human-recognizable labels other
 * media players show — "4K", "Dolby Vision", "Dolby Atmos", "Direct Play" — instead of codec strings
 * and pipeline notation. Kept framework-free and side-effect-free so they are unit tested in isolation
 * and reused by both the props extractor and the player UI.
 */

/** How the server is delivering the stream, in order of increasing work. */
export type StreamMethod = "direct" | "remux" | "transcode";

/** Minimal shape of a Jellyfin video stream this module reads. */
export interface VideoStreamBadgeInput {
  Codec?: string | null;
  Width?: number | null;
  Height?: number | null;
  VideoRange?: string | null;
  VideoRangeType?: string | null;
  DvProfile?: number | null;
  Hdr10PlusPresentFlag?: boolean | null;
}

/** Minimal shape of a Jellyfin audio stream this module reads. */
export interface AudioStreamBadgeInput {
  Codec?: string | null;
  Channels?: number | null;
  DisplayTitle?: string | null;
  Profile?: string | null;
}

function positiveInt(value: number | null | undefined): number {
  return typeof value === "number" && Number.isFinite(value) && value > 0 ? Math.round(value) : 0;
}

/**
 * Maps pixel dimensions to a marketing resolution tier ("4K", "1080p", …). Uses width OR height
 * thresholds so cinematic (letterboxed) sources — a 3840x1600 scope master — still read as 4K, while
 * 4:3 sources are caught by height. Returns null when both dimensions are unknown.
 */
export function resolutionBadge(
  width: number | null | undefined,
  height: number | null | undefined,
): string | null {
  const w = positiveInt(width);
  const h = positiveInt(height);
  if (w === 0 && h === 0) return null;
  if (w >= 7600 || h >= 4300) return "8K";
  if (w >= 3800 || h >= 2000) return "4K";
  if (w >= 2540 || h >= 1400) return "1440p";
  if (w >= 1800 || h >= 1000) return "1080p";
  if (w >= 1200 || h >= 700) return "720p";
  if (w >= 640 || h >= 480) return "480p";
  return "SD";
}

/**
 * Returns a friendly HDR format name, or null for standard dynamic range (SDR is the default and not
 * worth a badge). Dolby Vision wins over the HDR10 base layer it may carry, matching how the format is
 * marketed and how other players label it.
 */
export function dynamicRangeBadge(stream: VideoStreamBadgeInput | null | undefined): string | null {
  if (!stream) return null;
  const type = (stream.VideoRangeType ?? stream.VideoRange ?? "").trim().toUpperCase();
  if (stream.DvProfile != null || type.startsWith("DOVI") || type.includes("DOLBYVISION")) {
    return "Dolby Vision";
  }
  if (type.includes("HDR10PLUS") || type.includes("HDR10+") || stream.Hdr10PlusPresentFlag) {
    return "HDR10+";
  }
  if (type.includes("HDR10")) return "HDR10";
  if (type.includes("HLG")) return "HLG";
  if (type.includes("HDR")) return "HDR";
  return null;
}

/** Normalizes a codec id to the name viewers recognize. Unknown codecs are upper-cased as-is. */
export function videoCodecBadge(codec: string | null | undefined): string | null {
  const normalized = codec?.trim().toLowerCase();
  if (!normalized) return null;
  if (["h264", "avc", "avc1", "x264"].includes(normalized)) return "H.264";
  if (["h265", "hevc", "hvc1", "hev1", "x265"].includes(normalized)) return "HEVC";
  if (normalized === "av1") return "AV1";
  if (normalized === "vp9") return "VP9";
  if (normalized === "vp8") return "VP8";
  if (normalized === "vc1") return "VC-1";
  if (normalized === "mpeg2video") return "MPEG-2";
  return normalized.toUpperCase();
}

/** Maps a channel count to a speaker-layout label ("5.1", "Stereo", …). */
export function channelLayoutLabel(channels: number | null | undefined): string | null {
  const count = positiveInt(channels);
  if (count === 0) return null;
  switch (count) {
    case 1:
      return "Mono";
    case 2:
      return "Stereo";
    case 3:
      return "2.1";
    case 4:
      return "4.0";
    case 5:
      return "5.0";
    case 6:
      return "5.1";
    case 7:
      return "6.1";
    case 8:
      return "7.1";
    case 10:
      return "9.1";
    default:
      return `${count} ch`;
  }
}

function audioCodecName(codec: string | null | undefined): string | null {
  const normalized = codec?.trim().toLowerCase();
  if (!normalized) return null;
  if (normalized === "eac3") return "Dolby Digital+";
  if (normalized === "ac3") return "Dolby Digital";
  if (normalized === "truehd" || normalized === "mlp") return "Dolby TrueHD";
  if (normalized === "dts" || normalized === "dca") return "DTS";
  if (normalized === "aac") return "AAC";
  if (normalized === "flac") return "FLAC";
  if (normalized === "alac") return "ALAC";
  if (normalized === "opus") return "Opus";
  if (normalized === "vorbis") return "Vorbis";
  if (normalized === "mp3") return "MP3";
  if (normalized === "mp2") return "MP2";
  if (normalized.startsWith("pcm")) return "PCM";
  return normalized.toUpperCase();
}

// Premium object-based / lossless formats are not distinguishable from the codec id alone (Atmos rides
// on E-AC3 or TrueHD; DTS:X and DTS-HD MA are DTS profiles), so they are sniffed from the human display
// title the server already assembled.
function premiumAudioName(displayTitle: string | null | undefined): string | null {
  const title = displayTitle?.toLowerCase() ?? "";
  if (!title) return null;
  if (title.includes("atmos")) return "Dolby Atmos";
  if (/dts[\s-]?x/.test(title)) return "DTS:X";
  if (/dts-hd\s*(ma|master)/.test(title)) return "DTS-HD MA";
  if (title.includes("dts-hd")) return "DTS-HD";
  return null;
}

/**
 * Builds a short audio descriptor like "Dolby Atmos 7.1", "Dolby Digital+ 5.1", or "AAC Stereo" from a
 * stream's codec, channel count, and display title. Returns null when there is nothing to describe.
 */
export function audioFormatBadge(stream: AudioStreamBadgeInput | null | undefined): string | null {
  if (!stream) return null;
  const base = premiumAudioName(stream.DisplayTitle) ?? audioCodecName(stream.Codec);
  if (!base) return null;
  const layout = channelLayoutLabel(stream.Channels);
  return layout ? `${base} ${layout}` : base;
}

/** Label and one-line explanation for each delivery method, mirroring Plex/Jellyfin terminology. */
export function playbackMethodBadge(method: StreamMethod): { label: string; hint: string } {
  switch (method) {
    case "direct":
      return { label: "Direct Play", hint: "Playing the original file as-is" };
    case "remux":
      return { label: "Direct Stream", hint: "Original video, repackaged for your browser" };
    case "transcode":
      return { label: "Transcoding", hint: "Converting the video for your browser" };
  }
}
