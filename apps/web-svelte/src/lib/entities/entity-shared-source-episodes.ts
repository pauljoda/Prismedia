import type {
  EntitySharedSourceEpisode,
  EntityThumbnail,
} from "$lib/api/generated/model";
import { ENTITY_KIND } from "./entity-codes";
import type { EntityThumbnailCard } from "./entity-thumbnail";

type SharedSourceEpisodeEntity = Pick<
  EntityThumbnail,
  "kind" | "title" | "sharedSourceEpisodes"
>;

function distinctSharedSourceEpisodes(
  entity: SharedSourceEpisodeEntity,
): EntitySharedSourceEpisode[] {
  const seen = new Set<string>();
  return (entity.sharedSourceEpisodes ?? []).filter((episode) => {
    const key = `${episode.seasonNumber ?? ""}:${episode.episodeNumber ?? ""}:${episode.title.trim()}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

export function sharedSourceEpisodeDisplayTitle(
  entity: SharedSourceEpisodeEntity,
): string {
  const titles = [...new Set(
    distinctSharedSourceEpisodes(entity)
      .map((episode) => episode.title.trim())
      .filter(Boolean),
  )];
  return titles.length > 1 ? titles.join(" + ") : entity.title;
}

export function sharedSourceEpisodeOverlay(
  entity: SharedSourceEpisodeEntity,
): EntityThumbnailCard["custom"] {
  if (entity.kind !== ENTITY_KIND.videoEpisode) return undefined;

  const episodes = distinctSharedSourceEpisodes(entity);
  const episodeNumbers = [...new Set(
    episodes
      .map((episode) => episode.episodeNumber)
      .filter((value): value is number => value != null),
  )];
  if (episodeNumbers.length < 2) return undefined;

  const seasonNumbers = [...new Set(
    episodes
      .map((episode) => episode.seasonNumber)
      .filter((value): value is number => value != null),
  )];
  const episodeLabel = episodeNumbers.map((value) => `E${value}`).join(" + ");
  const episodeTitle = episodeNumbers.join(" + ");
  const seasonNumber = seasonNumbers.length === 1 ? seasonNumbers[0] : null;
  return {
    bottomLeft: {
      label: seasonNumber != null ? `S${seasonNumber} ${episodeLabel}` : episodeLabel,
      title: seasonNumber != null
        ? `Season ${seasonNumber}, Episodes ${episodeTitle}`
        : `Episodes ${episodeTitle}`,
    },
  };
}

export function coalesceSharedSourceEpisodes(
  thumbnails: EntityThumbnail[],
): EntityThumbnail[] {
  const thumbnailById = new Map(thumbnails.map((thumbnail) => [thumbnail.id, thumbnail]));
  const emittedGroups = new Set<string>();

  return thumbnails.flatMap((thumbnail) => {
    const visibleMembers = (thumbnail.sharedSourceEpisodes ?? [])
      .filter((member) => thumbnailById.has(member.id));
    if (visibleMembers.length < 2) return [thumbnail];

    const groupKey = visibleMembers.map((member) => member.id).sort().join(":");
    if (emittedGroups.has(groupKey)) return [];
    emittedGroups.add(groupKey);
    return [visibleMembers.map((member) => thumbnailById.get(member.id)).find(Boolean) ?? thumbnail];
  });
}
