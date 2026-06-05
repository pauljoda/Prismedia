import type { PlayerQualityRung } from "$lib/components/video-player-types";

/**
 * Remembers the viewer's manual quality choice per device (like Jellyfin's "remember quality"), as a
 * max video bitrate in bits per second, or the sentinels "auto"/"direct". Stored in localStorage so a
 * capped quality follows the viewer across every video on this device.
 */
export type QualityPreference = "auto" | "direct" | number;

const STORAGE_KEY = "prismedia.player.qualityPreference";

/** Reads the saved quality preference, defaulting to "auto" when unset or unavailable. */
export function readQualityPreference(): QualityPreference {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw === null) return "auto";
    if (raw === "auto" || raw === "direct") return raw;
    const bitrate = Number(raw);
    return Number.isFinite(bitrate) && bitrate > 0 ? bitrate : "auto";
  } catch {
    return "auto";
  }
}

/** Persists the viewer's quality preference for this device. */
export function writeQualityPreference(preference: QualityPreference): void {
  try {
    localStorage.setItem(STORAGE_KEY, String(preference));
  } catch {
    // Private mode / storage disabled — quality just won't be remembered.
  }
}

/**
 * Resolves a saved preference against the tiers available for the current source, returning the rung
 * name to pin — or null to mean "Auto" (no specific rung). A numeric cap picks the highest tier at or
 * below the cap; if every tier is above the cap, the lowest tier is used so the cap is still honored.
 */
export function resolvePreferredRung(
  preference: QualityPreference,
  rungs: readonly PlayerQualityRung[],
): string | null {
  if (typeof preference !== "number" || rungs.length === 0) return null;
  const atOrBelow = rungs
    .filter((rung) => rung.bitrate <= preference)
    .sort((a, b) => b.bitrate - a.bitrate);
  if (atOrBelow.length > 0) return atOrBelow[0]!.name;
  // Cap is below even the smallest tier — pin the lowest available tier.
  return [...rungs].sort((a, b) => a.bitrate - b.bitrate)[0]!.name;
}
