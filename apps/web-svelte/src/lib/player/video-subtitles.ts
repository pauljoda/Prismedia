import type { SubtitleCue, VideoSubtitleTrack } from "./subtitle-types";

const cueCache = new Map<string, Promise<{ cues: SubtitleCue[] }>>();

export async function fetchVideoSubtitleCues(
  track: Pick<VideoSubtitleTrack, "id" | "url">,
): Promise<{ cues: SubtitleCue[] }> {
  if (!track.url) return { cues: [] };

  const cacheKey = `${track.id}:${track.url}`;
  const cached = cueCache.get(cacheKey);
  if (cached) return cached;

  const pending = fetch(track.url, { cache: "no-store" })
    .then(async (response) => {
      if (!response.ok) {
        throw new Error(`Subtitle cues failed (${response.status})`);
      }
      return response.text();
    })
    .then((vtt) => ({ cues: parseWebVttCues(vtt) }));

  cueCache.set(cacheKey, pending);
  return pending;
}

export async function fetchVideoSubtitleSource(sourceUrl: string): Promise<string> {
  const response = await fetch(sourceUrl, { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`Subtitle source failed (${response.status})`);
  }
  return response.text();
}

export function parseWebVttCues(vtt: string): SubtitleCue[] {
  const cleaned = vtt.replace(/^\uFEFF/, "").replace(/\r\n/g, "\n");
  const blocks = cleaned.split(/\n{2,}/);
  const cues: SubtitleCue[] = [];

  for (const block of blocks) {
    const lines = block.split("\n").filter((line) => line.trim().length > 0);
    if (lines.length === 0) continue;

    if (/^WEBVTT/.test(lines[0]!)) continue;
    if (/^NOTE(\s|$)/.test(lines[0]!)) continue;
    if (/^STYLE(\s|$)/.test(lines[0]!)) continue;

    let timingIndex = 0;
    if (!lines[timingIndex]!.includes("-->")) timingIndex += 1;
    if (timingIndex >= lines.length) continue;

    const match = lines[timingIndex]!.match(
      /^(\d{2,}:\d{2}:\d{2}\.\d{3}|\d{2}:\d{2}\.\d{3})\s*-->\s*(\d{2,}:\d{2}:\d{2}\.\d{3}|\d{2}:\d{2}\.\d{3})/,
    );
    if (!match) continue;

    const text = lines
      .slice(timingIndex + 1)
      .join("\n")
      .replace(/<[^>]+>/g, "")
      .trim();
    if (!text) continue;

    cues.push({
      start: parseWebVttTimestamp(match[1]!),
      end: parseWebVttTimestamp(match[2]!),
      text,
    });
  }

  return cues;
}

function parseWebVttTimestamp(timestamp: string): number {
  const parts = timestamp.split(":");
  if (parts.length === 3) {
    const [hours, minutes, rest] = parts;
    const [seconds, millis] = rest!.split(".");
    return (
      Number(hours) * 3600 +
      Number(minutes) * 60 +
      Number(seconds) +
      Number(millis) / 1000
    );
  }

  if (parts.length === 2) {
    const [minutes, rest] = parts;
    const [seconds, millis] = rest!.split(".");
    return Number(minutes) * 60 + Number(seconds) + Number(millis) / 1000;
  }

  return 0;
}
