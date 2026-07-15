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
 * Presents a container cursor like book progress: the cursor selects the episode to continue,
 * while a partial episode contributes its own watched fraction to the overall percentage.
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
  const episodeFraction = completed ? 1 : playbackFraction(currentEpisode);
  const percent = completed ? 100 : ((index + episodeFraction) / total) * 100;

  return {
    episodeId: progress.currentEntityId,
    episodeLabel: currentEpisode?.title ?? null,
    index,
    total,
    percent: Math.min(100, Math.max(0, percent)),
    positionLabel: `Episode ${index + 1} of ${total}`,
    completed,
    canContinue: !completed,
  };
}

/** Adapts the shared thumbnail read model into the small progress policy input. */
export function videoProgressEpisodeFromCard(
  card: EntityThumbnailCard | null | undefined,
): VideoProgressEpisode | null {
  if (!card) return null;
  const playback = getCapability(card.entity.capabilities, CAPABILITY_KIND.playback);
  const technical = getTechnicalCapability(card.entity.capabilities);
  const thumbnailFraction = numberValue(card.progress);
  return {
    id: card.entity.id,
    title: card.entity.title,
    resumeSeconds: thumbnailFraction == null
      ? Math.max(0, numberValue(playback?.resumeSeconds) ?? 0)
      : Math.min(1, Math.max(0, thumbnailFraction)),
    durationSeconds: thumbnailFraction == null ? durationToSeconds(technical?.duration) : 1,
    completedAt: playback?.completedAt ?? (thumbnailFraction === 1 ? "completed" : null),
  };
}

function playbackFraction(episode: VideoProgressEpisode | null): number {
  if (!episode || episode.completedAt != null) return episode?.completedAt != null ? 1 : 0;
  if (!episode.durationSeconds || episode.durationSeconds <= 0) return 0;
  return Math.min(1, Math.max(0, episode.resumeSeconds / episode.durationSeconds));
}
