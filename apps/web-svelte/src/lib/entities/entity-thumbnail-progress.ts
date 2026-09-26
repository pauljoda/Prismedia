import { CAPABILITY_KIND } from "$lib/api/generated/codes";
import { getCapability, getTechnicalCapability } from "$lib/api/capabilities";
import type { EntityCard, EntityThumbnail } from "$lib/api/generated/model";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import { durationToSeconds, numberValue } from "$lib/utils/format";

/** The progress meters a thumbnail draws. */
export interface ThumbnailProgress {
  /** Fraction watched/read in 0..1; for a Separate Book, reading alone. */
  progress: number | null;
  /** Whether the thumbnail draws reading and listening as two meters. */
  separateProgress: boolean;
  /** Fraction (0..1) of the audio listened, for a Separate Book. */
  listeningProgress: number | null;
}

const clamp01 = (value: number): number => Math.min(1, Math.max(0, value));

function fractionOrNull(value: number | string | null | undefined): number | null {
  const fraction = numberValue(value);
  return fraction != null && Number.isFinite(fraction) ? clamp01(fraction) : null;
}

function isFullEntityCard(entity: EntityCard | EntityThumbnail): entity is EntityCard {
  return "capabilities" in entity;
}

/**
 * Resolves a thumbnail's progress meters. An unfinished Book that keeps reading and listening
 * separate carries two fractions (reading and listening), each from its own exact position; every
 * other entity carries one. Lightweight rows carry them precomputed; full entity cards read the
 * progress capability's separate block.
 */
export function thumbnailProgress(entity: EntityCard | EntityThumbnail): ThumbnailProgress {
  const separate = isFullEntityCard(entity)
    ? getCapability(entity.capabilities, CAPABILITY_KIND.progress)?.separate ?? null
    : null;
  if (separate) {
    const reading = fractionOrNull(separate.readingPercent);
    return {
      progress: reading != null && reading > 0 ? reading : null,
      separateProgress: true,
      listeningProgress: fractionOrNull(separate.listeningPercent),
    };
  }
  if (!isFullEntityCard(entity) && entity.progressSeparate) {
    return {
      progress: fractionOrNull(entity.progress),
      separateProgress: true,
      listeningProgress: fractionOrNull(entity.listeningProgress),
    };
  }
  return { progress: singleProgress(entity), separateProgress: false, listeningProgress: null };
}

/**
 * Resolves the single 0..1 progress meter fraction. Lightweight browse rows carry a precomputed
 * `progress` field; full entity cards derive it from the shared playback capability (videos:
 * completed → 1, else resume position over known runtime) or progress capability (books and ordered
 * containers: completed → 1, else independent consumed coverage). Returns null when there is nothing
 * to show.
 */
function singleProgress(entity: EntityCard | EntityThumbnail): number | null {
  if (!isFullEntityCard(entity)) {
    return fractionOrNull(entity.progress);
  }

  const capabilities = entity.capabilities;
  const progress = getCapability(capabilities, CAPABILITY_KIND.progress);
  // A Linked Book keeps one meter: its reading cursor, which listening moves only inside exactly
  // paired chapters. A Separate Book draws two meters (see thumbnailProgress).
  if (entity.kind === ENTITY_KIND.book && progress) {
    if (progress.completedAt) return 1;
    const consumedPercent = numberValue(progress.consumedPercent);
    if (consumedPercent != null && consumedPercent > 0) return clamp01(consumedPercent);
    const total = numberValue(progress.total) ?? 0;
    const index = numberValue(progress.index) ?? 0;
    return total > 0 && index > 0 ? clamp01(index / total) : null;
  }

  const consumption = getCapability(capabilities, CAPABILITY_KIND.consumption);
  if (consumption) {
    if (consumption.completedAt) return 1;
    const resumeSeconds = numberValue(consumption.resumeSeconds) ?? 0;
    const durationSeconds = durationToSeconds(getTechnicalCapability(capabilities)?.duration ?? null) ?? 0;
    return resumeSeconds > 0 && durationSeconds > 0 ? clamp01(resumeSeconds / durationSeconds) : null;
  }

  if (progress) {
    if (progress.completedAt) return 1;
    const consumedPercent = numberValue(progress.consumedPercent);
    if (consumedPercent != null && consumedPercent > 0) return clamp01(consumedPercent);
    const total = numberValue(progress.total) ?? 0;
    const index = numberValue(progress.index) ?? 0;
    return total > 0 && index > 0 ? clamp01(index / total) : null;
  }

  return null;
}
