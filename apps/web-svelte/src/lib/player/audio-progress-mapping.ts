import { CONSUMPTION_ACTIVITY_KIND, PROGRESS_UNIT } from "$lib/api/generated/codes";
import type { PlaybackProgressMapping } from "$lib/api/generated/model";

/** Canonical Entity progress produced from one mapped item in the shared audio player. */
export interface AudioProgressUpdate {
  currentEntityId: string;
  unit: PlaybackProgressMapping["unit"];
  index: number;
  total: number;
  mode: PlaybackProgressMapping["mode"];
  location: string | null;
  completed: boolean | null;
  activitySeconds: number | null;
  activityKind: typeof CONSUMPTION_ACTIVITY_KIND.listening | undefined;
}

function clampFraction(value: number): number {
  if (!Number.isFinite(value)) return 0;
  return Math.max(0, Math.min(1, value));
}

function numberValue(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined) return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Selects the progress window that owns one physical playback position. */
export function resolvePlaybackProgressMappingForTime(
  mappings: readonly PlaybackProgressMapping[],
  itemId: string,
  offsetSeconds: number,
): PlaybackProgressMapping | null {
  const candidates = mappings.filter((mapping) => mapping.itemId === itemId);
  const windowed = candidates.filter((mapping) =>
    numberValue(mapping.sourceStartSeconds) !== null &&
    numberValue(mapping.sourceEndSeconds) !== null
  );
  const owned = windowed.findLast((mapping) => {
    const start = numberValue(mapping.sourceStartSeconds)!;
    const end = numberValue(mapping.sourceEndSeconds)!;
    return offsetSeconds >= start && offsetSeconds <= end;
  });
  return owned ?? candidates.find((mapping) =>
    numberValue(mapping.sourceStartSeconds) === null &&
    numberValue(mapping.sourceEndSeconds) === null
  ) ?? null;
}

/** Converts one local audio position into the owning Entity's canonical progress cursor. */
export function audioProgressUpdateForItem(
  mapping: PlaybackProgressMapping,
  offsetSeconds: number,
  durationSeconds: number,
  activitySeconds: number | null,
  completed: boolean,
): AudioProgressUpdate {
  const start = Number(mapping.startIndex);
  const end = Number(mapping.endIndex);
  const total = Number(mapping.total);
  const sourceStart = numberValue(mapping.sourceStartSeconds) ?? 0;
  const sourceEnd = numberValue(mapping.sourceEndSeconds) ?? durationSeconds;
  const sourceDuration = Math.max(0, sourceEnd - sourceStart);
  const fraction = sourceDuration > 0
    ? clampFraction((offsetSeconds - sourceStart) / sourceDuration)
    : 0;
  const index = mapping.unit === PROGRESS_UNIT.page
    ? Math.max(start, Math.min(end, Math.ceil(fraction * total) - 1))
    : Math.max(start, Math.min(end, Math.round(start + fraction * (end - start))));

  return {
    currentEntityId: mapping.currentEntityId,
    unit: mapping.unit,
    index,
    total,
    mode: mapping.mode,
    location: mapping.resourceLocation ?? null,
    completed: completed ? true : null,
    activitySeconds,
    activityKind: activitySeconds && activitySeconds > 0
      ? CONSUMPTION_ACTIVITY_KIND.listening
      : undefined,
  };
}
