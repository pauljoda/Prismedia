/** Coerce a mixed number/string value to a finite number, or null. */
export function numberValue(value: number | string | null | undefined): number | null {
  if (typeof value === "number") return Number.isFinite(value) ? value : null;
  if (typeof value !== "string" || value.trim() === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Like {@link numberValue}, but rejects zero and negative values. */
export function positiveNumberValue(value: number | string | null | undefined): number | null {
  const n = numberValue(value);
  return n !== null && n > 0 ? n : null;
}

/**
 * Format an "HH:MM:SS.xxx" duration string for display.
 *
 * @param includeSeconds When true (default), short durations render as "MM:SS"
 *   and long durations as "H:MM:SS". When false, short durations render as
 *   "MM:SS" and long durations render as "HH:MM" (seconds dropped).
 */
export function formatDurationString(
  value: string | null | undefined,
  includeSeconds = true,
): string | null {
  if (!value) return null;
  const [hours = "0", minutes = "0", seconds = "0"] = value.split(":");
  const roundedSeconds = seconds.split(".")[0] ?? "0";
  const isShort = hours === "00" || hours === "0";

  if (isShort) {
    return `${minutes.padStart(2, "0")}:${roundedSeconds.padStart(2, "0")}`;
  }
  if (includeSeconds) {
    return `${hours}:${minutes.padStart(2, "0")}:${roundedSeconds.padStart(2, "0")}`;
  }
  return `${hours.padStart(2, "0")}:${minutes.padStart(2, "0")}`;
}

/** Parse an "HH:MM:SS" duration string to total seconds. */
export function durationToSeconds(value: string | null | undefined): number | null {
  if (!value) return null;
  const [hours = "0", minutes = "0", seconds = "0"] = value.split(":");
  const total = Number(hours) * 3600 + Number(minutes) * 60 + Number(seconds);
  return Number.isFinite(total) ? total : null;
}

/** Normalize a string for loose comparison (lowercase, strip punctuation). */
export function normalized(value: string | null | undefined): string {
  return (value ?? "").toLowerCase().replaceAll(".", "").replaceAll("-", "").replaceAll("_", "");
}

/** Format an ISO timestamp as a relative time string ("just now", "3m ago", "2h ago", "5d ago"). */
export function formatRelativeTime(value: string | null, short = false): string {
  if (!value) return "Never";
  const diffMs = Date.now() - new Date(value).getTime();
  const diffMinutes = Math.max(0, Math.floor(diffMs / 60_000));
  if (diffMinutes < 1) return short ? "now" : "just now";
  if (diffMinutes < 60) return short ? `${diffMinutes}m` : `${diffMinutes}m ago`;
  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) return short ? `${diffHours}h` : `${diffHours}h ago`;
  const diffDays = Math.floor(diffHours / 24);
  return short ? `${diffDays}d` : `${diffDays}d ago`;
}

/** Map a video height to a short resolution label ("4K", "1080p", etc.). */
export function formatResolutionLabel(height: number): string | null {
  if (!Number.isFinite(height) || height <= 0) return null;
  if (height >= 2160) return "4K";
  if (height >= 1440) return "1440p";
  if (height >= 1080) return "1080p";
  if (height >= 720) return "720p";
  if (height >= 480) return "480p";
  return `${height}p`;
}
