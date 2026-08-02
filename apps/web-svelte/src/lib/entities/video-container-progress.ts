import { getCapability, getTechnicalCapability } from "$lib/api/capabilities";
import { CAPABILITY_KIND } from "$lib/api/generated/codes";
import type { EntityCapabilityProgressCapability } from "$lib/api/generated/model";
import { durationToSeconds, numberValue } from "$lib/utils/format";
import type { EntityThumbnailCard } from "./entity-thumbnail";

/** Minimal episode state needed to blend an in-episode position into a container cursor. */
export interface VideoProgressEpisode {
  id: string;
  title: string;
  resumeSeconds: number;
  durationSeconds: number | null;
  completedAt: string | null;
}

/** Display policy shared by series and season progress cards. */
export interface VideoContainerProgressDisplay {
  episodeId: string;
  episodeLabel: string | null;
  index: number;
  total: number;
  percent: number;
  positionLabel: string;
  completed: boolean;
  canContinue: boolean;
}

/**
 * Presents the two independent facts a container needs: the most recently active episode is the
 * continue cursor, while the watched percentage comes from completed-episode coverage. Revisiting
 * an earlier episode therefore moves "Current" backward without erasing already watched coverage.
 */
export function videoContainerProgressDisplay(
  progress: EntityCapabilityProgressCapability | null | undefined,
  episode: VideoProgressEpisode | null | undefined,
): VideoContainerProgressDisplay | null {
  if (!progress?.currentEntityId) return null;

  const total = Math.max(0, numberValue(progress.total) ?? 0);
  if (total === 0) return null;

  const index = Math.min(total - 1, Math.max(0, numberValue(progress.index) ?? 0));
  const completed = progress.completedAt != null;
  const currentEpisode = episode?.id === progress.currentEntityId ? episode : null;
  const consumedPercent = numberValue(progress.consumedPercent);
  const consumedCount = Math.max(0, numberValue(progress.consumedCount) ?? 0);
  const percent = completed
    ? 100
    : consumedPercent == null
      ? (consumedCount / total) * 100
      : consumedPercent * 100;

  return {
    episodeId: progress.currentEntityId,
    episodeLabel: [
      currentEpisode?.title ?? null,
      `${Math.min(total, consumedCount)} of ${total} watched`,
    ].filter(Boolean).join(" · "),
    index,
    total,
    percent: Math.min(100, Math.max(0, percent)),
    positionLabel: `Current · Episode ${index + 1} of ${total}`,
    completed,
    canContinue: !completed,
  };
}

/** Adapts the shared thumbnail read model into the small progress policy input. */
export function videoProgressEpisodeFromCard(
  card: EntityThumbnailCard | null | undefined,
): VideoProgressEpisode | null {
  if (!card) return null;
  const consumption = getCapability(card.entity.capabilities, CAPABILITY_KIND.consumption);
  const technical = getTechnicalCapability(card.entity.capabilities);
  const thumbnailFraction = numberValue(card.progress);
  return {
    id: card.entity.id,
    title: card.entity.title,
    resumeSeconds: thumbnailFraction == null
      ? Math.max(0, numberValue(consumption?.resumeSeconds) ?? 0)
      : Math.min(1, Math.max(0, thumbnailFraction)),
    durationSeconds: thumbnailFraction == null ? durationToSeconds(technical?.duration) : 1,
    completedAt: consumption?.completedAt ?? (thumbnailFraction === 1 ? "completed" : null),
  };
}
