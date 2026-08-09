import type {
  EntityCard,
  EntityCreditMetadata,
  EntityThumbnail,
} from "$lib/api/generated/model";
import {
  fetchEntityThumbnails,
  type EntityThumbnailRequestOptions,
} from "$lib/api/entities";
import { getCreditsCapability } from "$lib/api/capabilities";
import { getRelationshipIds } from "./entity-children";
import type { EntityDetailCredit, EntityDetailTag } from "./entity-detail";
import { entityCardToThumbnailCard } from "./entity-grid";
import { coalesceSharedSourceEpisodes } from "./entity-shared-source-episodes";
import type { EntityThumbnailCard } from "./entity-thumbnail";
import {
  ENTITY_KIND,
  RELATIONSHIP_CODE,
  resolveEntityHref,
  type EntityKindCode,
} from "./entity-codes";

type EntityRelationshipSource = Pick<EntityCard, "relationships">;

export type { EntityThumbnailCard };

/** Fetches thumbnails for IDs and restores the caller's relationship order. */
export async function fetchOrderedEntityThumbnails(
  ids: string[],
  options?: EntityThumbnailRequestOptions,
): Promise<EntityThumbnail[]> {
  const thumbnails = await fetchEntityThumbnails(ids, options);
  const byId = new Map(thumbnails.map((thumbnail) => [thumbnail.id, thumbnail]));
  return ids.map((id) => byId.get(id)).filter((thumbnail): thumbnail is EntityThumbnail => Boolean(thumbnail));
}

export function relationshipIds(
  entity: EntityRelationshipSource | null | undefined,
  code: string,
  kind?: EntityKindCode,
): string[] {
  return getRelationshipIds(entity, code, kind);
}

export function thumbnailsToCards(
  thumbnails: EntityThumbnail[],
  options: {
    hrefFor?: (thumbnail: EntityThumbnail) => string | undefined;
    groupSharedSourceEpisodes?: boolean;
  } = {},
): EntityThumbnailCard[] {
  const displayedThumbnails = options.groupSharedSourceEpisodes
    ? coalesceSharedSourceEpisodes(thumbnails)
    : thumbnails;
  return displayedThumbnails.map((thumbnail) =>
    entityCardToThumbnailCard(thumbnail, options.hrefFor?.(thumbnail)),
  );
}

export function tagsFromThumbnails(thumbnails: EntityThumbnail[]): EntityDetailTag[] {
  return thumbnails.map((thumbnail) => ({
    id: thumbnail.id,
    kind: thumbnail.kind,
    title: thumbnail.title,
    href: resolveEntityHref(thumbnail.kind, thumbnail.id) ?? null,
  }));
}

export function firstRelationshipThumbnail(
  thumbnails: EntityThumbnail[],
  kind: EntityKindCode,
): EntityThumbnail | null {
  return thumbnails.find((thumbnail) => thumbnail.kind === kind) ?? null;
}

/**
 * Builds the shared detail-card credit view models that feed both the credits display
 * section and the credits edit draft. Carrying the full role/character lists here is what
 * lets metadata saves round-trip credits losslessly through the full-replace patch.
 */
export function detailCreditsFromThumbnails(
  thumbnails: EntityThumbnail[],
  metadata: EntityCreditMetadata[] = [],
): EntityDetailCredit[] {
  const metadataByPersonId = new Map(metadata.map((item) => [item.personId, item]));
  return thumbnails.map((thumbnail) => {
    const item = metadataByPersonId.get(thumbnail.id);
    return {
      id: thumbnail.id,
      kind: thumbnail.kind,
      title: thumbnail.title,
      thumbnail: entityCardToThumbnailCard(thumbnail).cover?.src ?? null,
      roles: item?.roles ?? (item?.role ? [item.role] : []),
      characters: item?.characters ?? (item?.character ? [item.character] : []),
    };
  });
}

/** Builds the detail-card studio view model from the studio relationship thumbnail. */
export function detailStudioFromThumbnails(thumbnails: EntityThumbnail[]): EntityDetailCredit | null {
  const thumbnail = thumbnails[0];
  if (!thumbnail) return null;
  return {
    id: thumbnail.id,
    kind: thumbnail.kind,
    title: thumbnail.title,
    thumbnail: entityCardToThumbnailCard(thumbnail).cover?.src ?? null,
    roles: [],
    characters: [],
  };
}

export async function hydrateStandardRelationshipThumbnails(
  entity: EntityRelationshipSource,
  options?: EntityThumbnailRequestOptions,
): Promise<{
  cast: EntityThumbnail[];
  studio: EntityThumbnail[];
  tags: EntityThumbnail[];
}> {
  const castIds = relationshipIds(entity, RELATIONSHIP_CODE.cast, ENTITY_KIND.person);
  const studioIds = relationshipIds(entity, RELATIONSHIP_CODE.studio, ENTITY_KIND.studio);
  const tagIds = relationshipIds(entity, RELATIONSHIP_CODE.tags, ENTITY_KIND.tag);
  const all = await fetchOrderedEntityThumbnails([...studioIds, ...castIds, ...tagIds], options);
  const byId = new Map(all.map((thumbnail) => [thumbnail.id, thumbnail]));

  return {
    cast: castIds.map((id) => byId.get(id)).filter((thumbnail): thumbnail is EntityThumbnail => Boolean(thumbnail)),
    studio: studioIds.map((id) => byId.get(id)).filter((thumbnail): thumbnail is EntityThumbnail => Boolean(thumbnail)),
    tags: tagIds.map((id) => byId.get(id)).filter((thumbnail): thumbnail is EntityThumbnail => Boolean(thumbnail)),
  };
}

export async function hydrateStandardRelationshipCards(
  entity: EntityRelationshipSource & Pick<EntityCard, "capabilities">,
  options?: EntityThumbnailRequestOptions,
): Promise<{
  relationshipTags: EntityDetailTag[];
  credits: EntityDetailCredit[];
  studio: EntityDetailCredit | null;
}> {
  const relationships = await hydrateStandardRelationshipThumbnails(entity, options);
  return {
    relationshipTags: tagsFromThumbnails(relationships.tags),
    credits: detailCreditsFromThumbnails(
      relationships.cast,
      getCreditsCapability(entity.capabilities)?.items ?? [],
    ),
    studio: detailStudioFromThumbnails(relationships.studio),
  };
}
