export interface TrickplayFrame {
  start: number;
  end: number;
  x: number;
  y: number;
  width: number;
  height: number;
  url: string;
}

const trickplayCache = new Map<string, Promise<TrickplayFrame[]>>();

function parseTimestamp(value: string) {
  const [timePart, msPart = "0"] = value.trim().split(".");
  const segments = timePart.split(":").map(Number);
  if (segments.length !== 3 || segments.some((segment) => Number.isNaN(segment))) {
    return 0;
  }

  const [hours, minutes, seconds] = segments;
  return hours * 3600 + minutes * 60 + seconds + Number(msPart) / 1000;
}

export function parseTrickplayVtt(raw: string): TrickplayFrame[] {
  const frames: TrickplayFrame[] = [];
  const lines = raw
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);

  for (let index = 0; index < lines.length - 1; index += 1) {
    const line = lines[index];
    if (!line.includes("-->")) {
      continue;
    }

    const [startRaw, endRaw] = line.split("-->").map((part) => part.trim());
    const assetLine = lines[index + 1];
    const [url, fragment] = assetLine.split("#xywh=");
    if (!fragment) {
      continue;
    }

    const [x, y, width, height] = fragment.split(",").map(Number);
    if ([x, y, width, height].some((value) => Number.isNaN(value))) {
      continue;
    }

    frames.push({
      start: parseTimestamp(startRaw),
      end: parseTimestamp(endRaw),
      x,
      y,
      width,
      height,
      url,
    });
  }

  return frames;
}

export function parseTrickplayImagePlaylist(raw: string, playlistUrl: string): TrickplayFrame[] {
  const lines = raw
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
  const tileLine = lines.find((line) => line.startsWith("#EXT-X-TILES:"));
  if (!tileLine) return [];

  const resolution = tileLine.match(/RESOLUTION=(\d+)x(\d+)/);
  const layout = tileLine.match(/LAYOUT=(\d+)x(\d+)/);
  const duration = tileLine.match(/DURATION=([0-9.]+)/);
  if (!resolution || !layout || !duration) return [];

  const width = Number(resolution[1]);
  const height = Number(resolution[2]);
  const columns = Number(layout[1]);
  const rows = Number(layout[2]);
  const interval = Number(duration[1]);
  if ([width, height, columns, rows, interval].some((value) => !Number.isFinite(value) || value <= 0)) {
    return [];
  }

  const frames: TrickplayFrame[] = [];
  let pendingDuration = columns * rows * interval;
  for (const line of lines) {
    if (line.startsWith("#EXTINF:")) {
      const parsed = Number(line.slice("#EXTINF:".length).replace(",", ""));
      pendingDuration = Number.isFinite(parsed) && parsed > 0 ? parsed : pendingDuration;
      continue;
    }
    if (line.startsWith("#")) continue;

    const baseUrl = typeof globalThis.location === "object"
      ? new URL(playlistUrl, globalThis.location.href).toString()
      : playlistUrl;
    const tileUrl = new URL(line, baseUrl).toString();
    const cells = columns * rows;
    const cellDuration = pendingDuration / cells;
    const baseStart = frames.length * cellDuration;
    for (let index = 0; index < cells; index += 1) {
      const x = (index % columns) * width;
      const y = Math.floor(index / columns) * height;
      const start = baseStart + index * cellDuration;
      frames.push({
        start,
        end: start + cellDuration,
        x,
        y,
        width,
        height,
        url: tileUrl,
      });
    }
  }

  return frames;
}

export async function loadTrickplayFrames(mapUrl: string): Promise<TrickplayFrame[]> {
  const cached = trickplayCache.get(mapUrl);
  if (cached) {
    return cached;
  }

  const pending = fetch(mapUrl)
    .then((response) => {
      if (!response.ok) {
        throw new Error(`Failed to load trickplay map (${response.status})`);
      }
      return response.text();
    })
    .then((text) => (
      text.includes("#EXT-X-IMAGES-ONLY")
        ? parseTrickplayImagePlaylist(text, mapUrl)
        : parseTrickplayVtt(text)
    ));

  trickplayCache.set(mapUrl, pending);
  return pending;
}

export function findFrameAtTime(frames: TrickplayFrame[], time: number): number {
  const index = frames.findIndex((frame) => time >= frame.start && time < frame.end);
  if (index !== -1) return index;
  // Fallback: clamp to last frame
  return Math.max(0, frames.length - 1);
}

/**
 * Convert a time value to a pixel position on the film strip track by
 * finding the matching frame and interpolating within it. This keeps
 * the playhead locked to the correct frame regardless of any mismatch
 * between video duration and VTT time ranges.
 */
export function timeToTrackPosition(
  frames: TrickplayFrame[],
  time: number,
  frameWidth: number,
): number {
  const idx = findFrameAtTime(frames, time);
  const frame = frames[idx];
  const span = frame.end - frame.start;
  const fraction = span > 0 ? Math.max(0, Math.min(1, (time - frame.start) / span)) : 0;
  return (idx + fraction) * frameWidth;
}
