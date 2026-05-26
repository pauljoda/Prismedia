export function formatTime(seconds: number): string {
  const safe = Number.isFinite(seconds) ? Math.max(0, seconds) : 0;
  const h = Math.floor(safe / 3600);
  const m = Math.floor((safe % 3600) / 60);
  const s = Math.floor(safe % 60);
  if (h > 0) return `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
  return `${m}:${String(s).padStart(2, "0")}`;
}

export function formatBandwidth(bps: number | null): string {
  if (!bps || !Number.isFinite(bps)) return "—";
  if (bps >= 1_000_000) {
    const mbps = bps / 1_000_000;
    return `${Number.isInteger(mbps) ? mbps.toFixed(0) : mbps.toFixed(1)} Mbps`;
  }
  return `${Math.round(bps / 1_000)} Kbps`;
}

export function formatDimensions(
  width: number | null | undefined,
  height: number | null | undefined,
): string | null {
  const safeWidth = typeof width === "number" && Number.isFinite(width) && width > 0
    ? Math.round(width)
    : null;
  const safeHeight = typeof height === "number" && Number.isFinite(height) && height > 0
    ? Math.round(height)
    : null;
  if (safeWidth && safeHeight) return `${safeWidth}x${safeHeight}`;
  return null;
}

export function languageLabel(language: string): string {
  if (!language || language === "und") return "Unknown";
  try {
    const displayNames = new Intl.DisplayNames(undefined, { type: "language" });
    return displayNames.of(language) ?? language.toUpperCase();
  } catch {
    return language.toUpperCase();
  }
}

export function rangeProgress(value: number, min: number, max: number): string {
  const pct = max > min ? ((value - min) / (max - min)) * 100 : 0;
  return `${Math.min(100, Math.max(0, pct))}%`;
}
