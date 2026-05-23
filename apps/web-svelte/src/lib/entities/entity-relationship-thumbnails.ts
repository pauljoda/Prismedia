import type {
  EntityCard,
  EntityCreditMetadata,
  EntityThumbnail,
} from "$lib/api/generated/model";
import { fetchEntityThumbnails } from "$lib/api/prismedia";
import { getRelationshipIds } from "./entity-children";
import { creditSubtitle, type EntityCredit } from "./entity-credits";
import type { EntityDetailTag } from "./entity-detail";
import { entityCardToThumbnailCard } from "./entity-grid";
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
export async function fetchOrderedEntityThumbnails(ids: string[]): Promise<EntityThumbnail[]> {
  const thumbnails = await fetchEntityThumbnails(ids);
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
    subtitleFor?: (thumbnail: EntityThumbnail) => string | undefined;
  } = {},
): EntityThumbnailCard[] {
  return thumbnails.map((thumbnail) => ({
    ...entityCardToThumbnailCard(thumbnail, options.hrefFor?.(thumbnail)),
    subtitle: options.subtitleFor?.(thumbnail),
  }));
}

export function tagsFromThumbnails(thumbnails: EntityThumbnail[]): EntityDetailTag[] {
  return thumbnails.map((thumbnail) => ({
    id: thumbnail.id,
    kind: thumbnail.kind,
    title: thumbnail.title,
    href: resolveEntityHref(thumbnail.kind, thumbnail.id) ?? null,
  }));
}

export function creditCardsFromThumbnails(
  thumbnails: EntityThumbnail[],
  metadata: EntityCreditMetadata[] = [],
): EntityThumbnailCard[] {
  const metadataByPersonId = new Map(metadata.map((item) => [item.personId, item]));
  return thumbnailsToCards(thumbnails, {
    subtitleFor: (thumbnail) => {
      const item = metadataByPersonId.get(thumbnail.id);
      const credit: EntityCredit = {
        character: item?.character ?? null,
        role: item?.role ?? null,
        person: {
          id: thumbnail.id,
          kind: thumbnail.kind,
          title: thumbnail.title,
          thumbnailUrl: thumbnail.coverUrl,
        },
      };
      return creditSubtitle(credit);
    },
  });
}

export function firstRelationshipThumbnail(
  thumbnails: EntityThumbnail[],
  kind: EntityKindCode,
): EntityThumbnail | null {
  return thumbnails.find((thumbnail) => thumbnail.kind === kind) ?? null;
}

export async function hydrateStandardRelationshipThumbnails(
  entity: EntityRelationshipSource,
): Promise<{
  cast: EntityThumbnail[];
  studio: EntityThumbnail[];
  tags: EntityThumbnail[];
}> {
  const castIds = relationshipIds(entity, RELATIONSHIP_CODE.cast, ENTITY_KIND.person);
  const studioIds = relationshipIds(entity, RELATIONSHIP_CODE.studio, ENTITY_KIND.studio);
  const tagIds = relationshipIds(entity, RELATIONSHIP_CODE.tags, ENTITY_KIND.tag);
  const all = await fetchOrderedEntityThumbnails([...studioIds, ...castIds, ...tagIds]);
  const byId = new Map(all.map((thumbnail) => [thumbnail.id, thumbnail]));

  return {
    cast: castIds.map((id) => byId.get(id)).filter((thumbnail): thumbnail is EntityThumbnail => Boolean(thumbnail)),
    studio: studioIds.map((id) => byId.get(id)).filter((thumbnail): thumbnail is EntityThumbnail => Boolean(thumbnail)),
    tags: tagIds.map((id) => byId.get(id)).filter((thumbnail): thumbnail is EntityThumbnail => Boolean(thumbnail)),
  };
}

export async function hydrateStandardRelationshipCards(
  entity: EntityRelationshipSource & { creditMetadata?: EntityCreditMetadata[] },
): Promise<{
  creditCards: EntityThumbnailCard[];
  relationshipTags: EntityDetailTag[];
  studioCards: EntityThumbnailCard[];
}> {
  const relationships = await hydrateStandardRelationshipThumbnails(entity);
  return {
    creditCards: creditCardsFromThumbnails(relationships.cast, entity.creditMetadata ?? []),
    relationshipTags: tagsFromThumbnails(relationships.tags),
    studioCards: thumbnailsToCards(relationships.studio),
  };
}
