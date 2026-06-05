/**
 * Builds the manual quality tiers the player offers, the way Jellyfin's web client does: a fixed list
 * of bitrate presets, capped to what the source actually contains, each mapping to a server rendition
 * the user can pin instead of letting the server pick. Picking a tier streams that specific rung
 * (a single transcode at that bitrate/resolution) — useful for capping quality on a weak connection.
 *
 * The preset names MUST stay in sync with the server's rendition names
 * (`JellyfinQualityPresetOptions` in HlsAssetService.Encoding.cs), because a tier resolves to the
 * variant playlist `/Videos/{id}/hls/{name}/stream.m3u8`. The capping logic mirrors the server's
 * `JellyfinQualityOptions`, so the client only ever offers rungs the server will actually produce.
 */

/** One selectable streaming quality tier. */
export interface QualityRung {
  /** Server rendition name (e.g. "8mbps"); maps to /Videos/{id}/hls/{name}/stream.m3u8. */
  name: string;
  /** Effective encoded height for this source (min of source height and the preset's cap). */
  height: number;
  /** Target video bitrate in bits per second. */
  bitrate: number;
  /** Viewer-facing label, e.g. "1080p · 8 Mbps". */
  label: string;
}

interface QualityPreset {
  name: string;
  maxHeight: number;
  bitrate: number;
}

// Descending by bitrate, mirroring the server preset table exactly.
const PRESETS: readonly QualityPreset[] = [
  { name: "120mbps", maxHeight: 2160, bitrate: 120_000_000 },
  { name: "80mbps", maxHeight: 2160, bitrate: 80_000_000 },
  { name: "60mbps", maxHeight: 2160, bitrate: 60_000_000 },
  { name: "40mbps", maxHeight: 2160, bitrate: 40_000_000 },
  { name: "20mbps", maxHeight: 2160, bitrate: 20_000_000 },
  { name: "15mbps", maxHeight: 1440, bitrate: 15_000_000 },
  { name: "10mbps", maxHeight: 1440, bitrate: 10_000_000 },
  { name: "8mbps", maxHeight: 1080, bitrate: 8_000_000 },
  { name: "6mbps", maxHeight: 1080, bitrate: 6_000_000 },
  { name: "4mbps", maxHeight: 720, bitrate: 4_000_000 },
  { name: "3mbps", maxHeight: 720, bitrate: 3_000_000 },
  { name: "1500kbps", maxHeight: 720, bitrate: 1_500_000 },
  { name: "720kbps", maxHeight: 480, bitrate: 720_000 },
  { name: "420kbps", maxHeight: 360, bitrate: 420_000 },
];

const EFFICIENT_CODECS = new Set(["hevc", "h265", "av1", "vp9"]);

function selectPresets(
  sourceBitrate: number | null | undefined,
  codec: string | null | undefined,
): QualityPreset[] {
  // Unknown bitrate: offer every preset, matching the server, which also produces all rungs.
  if (!sourceBitrate || !Number.isFinite(sourceBitrate) || sourceBitrate <= 0) {
    return [...PRESETS];
  }

  let comparable = sourceBitrate;
  const normalized = codec?.trim().toLowerCase();
  if (normalized && EFFICIENT_CODECS.has(normalized) && comparable <= 20_000_000) {
    comparable = Math.round(comparable * 1.5);
  }

  const selected: QualityPreset[] = [];
  // The smallest preset still above the source bitrate, so the top tier isn't a downgrade.
  const nextHigher = [...PRESETS].reverse().find((preset) => preset.bitrate > comparable);
  if (nextHigher) {
    selected.push(nextHigher);
  }
  selected.push(...PRESETS.filter((preset) => preset.bitrate <= comparable));
  return selected.length > 0 ? selected : [PRESETS[PRESETS.length - 1]!];
}

function friendlyBitrate(bitsPerSecond: number): string {
  if (bitsPerSecond >= 1_000_000) {
    const mbps = bitsPerSecond / 1_000_000;
    return `${Number.isInteger(mbps) ? mbps : mbps.toFixed(1)} Mbps`;
  }
  return `${Math.round(bitsPerSecond / 1000)} kbps`;
}

/**
 * Returns the selectable quality tiers for a source, highest first. Each tier's effective height is
 * capped to the source resolution, and the label reads like other apps ("1080p · 8 Mbps").
 */
export function qualityRungsForSource(
  sourceBitrate: number | null | undefined,
  sourceHeight: number | null | undefined,
  codec: string | null | undefined,
): QualityRung[] {
  const height = typeof sourceHeight === "number" && sourceHeight > 0 ? sourceHeight : null;
  return selectPresets(sourceBitrate, codec)
    .map((preset) => {
      const effectiveHeight = height ? Math.min(height, preset.maxHeight) : preset.maxHeight;
      return {
        name: preset.name,
        height: effectiveHeight,
        bitrate: preset.bitrate,
        label: `${effectiveHeight}p · ${friendlyBitrate(preset.bitrate)}`,
      };
    })
    .sort((a, b) => b.bitrate - a.bitrate);
}
