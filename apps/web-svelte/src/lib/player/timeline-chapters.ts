import type { VTTCueInit } from "vidstack";

export interface TimelineChapterMarker {
  id: string;
  time: number;
  endTime?: number | null;
  title: string;
}

export function buildTimelineChapterCues(
  markers: readonly TimelineChapterMarker[],
  duration: number,
): VTTCueInit[] {
  const safeDuration = Number.isFinite(duration) && duration > 0 ? duration : 0;
  if (safeDuration <= 0) return [];

  const ordered = markers
    .map((marker) => ({
      ...marker,
      time: clampSecond(marker.time, safeDuration),
      endTime: marker.endTime == null ? null : clampSecond(marker.endTime, safeDuration),
      title: marker.title.trim() || "Marker",
    }))
    .filter((marker) => marker.time < safeDuration)
    .sort((a, b) => a.time - b.time);

  const cues: VTTCueInit[] = [];
  for (let index = 0; index < ordered.length; index += 1) {
    const marker = ordered[index];
    const next = ordered[index + 1];
    const fallbackEnd = next?.time ?? safeDuration;
    const explicitEnd = marker.endTime && marker.endTime > marker.time
      ? marker.endTime
      : null;
    const endTime = Math.min(explicitEnd ?? fallbackEnd, safeDuration);

    if (endTime <= marker.time) continue;
    cues.push({
      startTime: marker.time,
      endTime,
      text: marker.title,
    });
  }

  return cues;
}

export function findTimelineChapterTitle(
  cues: readonly VTTCueInit[],
  time: number,
): string | null {
  if (!Number.isFinite(time)) return null;
  const cue = cues.find((candidate) => (
    time >= candidate.startTime && time < candidate.endTime && candidate.text.trim().length > 0
  ));
  return cue?.text.trim() || null;
}

function clampSecond(value: number, duration: number): number {
  if (!Number.isFinite(value)) return 0;
  return Math.max(0, Math.min(value, duration));
}
