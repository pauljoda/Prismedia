import { fetchEntity } from "$lib/api/entities";
import { ENTITY_KIND, isEntityKindCode, resolveEntityHref } from "./entity-codes";

export interface EntityRouteRecord {
  id: string;
  kind: string;
  parentEntityId: string | null;
}

export type EntityRouteFetcher = (id: string) => Promise<EntityRouteRecord>;

export async function resolveEntityHrefById(
  entityId: string,
  fetchRecord: EntityRouteFetcher = fetchEntity,
): Promise<string | null> {
  const entity = await fetchRecord(entityId);
  return resolveEntityHrefForRecord(entity, fetchRecord, new Set([entity.id]));
}

async function resolveEntityHrefForRecord(
  entity: EntityRouteRecord,
  fetchRecord: EntityRouteFetcher,
  seen: Set<string>,
): Promise<string | null> {
  if (entity.kind === ENTITY_KIND.bookPage) {
    const parent = await parentRecord(entity, fetchRecord, seen);
    return parent ? resolveEntityHrefForRecord(parent, fetchRecord, seen) : null;
  }

  if (entity.kind === ENTITY_KIND.bookVolume) {
    const book = await parentRecord(entity, fetchRecord, seen);
    return book?.kind === ENTITY_KIND.book ? `/books/${book.id}/volumes/${entity.id}` : null;
  }

  if (entity.kind === ENTITY_KIND.bookChapter) {
    const parent = await parentRecord(entity, fetchRecord, seen);
    if (!parent) return null;
    if (parent.kind === ENTITY_KIND.book) return `/books/${parent.id}/chapters/${entity.id}`;
    if (parent.kind !== ENTITY_KIND.bookVolume) return null;

    const book = await parentRecord(parent, fetchRecord, seen);
    return book?.kind === ENTITY_KIND.book ? `/books/${book.id}/chapters/${entity.id}` : null;
  }

  if (entity.kind === ENTITY_KIND.videoSeason || entity.kind === ENTITY_KIND.audioTrack) {
    const parent = await parentRecord(entity, fetchRecord, seen);
    if (!parent) return null;
    if (!isEntityKindCode(parent.kind)) return null;
    return resolveEntityHref(entity.kind, entity.id, {
      kind: parent.kind,
      id: parent.id,
    }) ?? null;
  }

  return resolveEntityHref(entity.kind, entity.id) ?? null;
}

async function parentRecord(
  entity: EntityRouteRecord,
  fetchRecord: EntityRouteFetcher,
  seen: Set<string>,
): Promise<EntityRouteRecord | null> {
  if (!entity.parentEntityId || seen.has(entity.parentEntityId)) return null;
  seen.add(entity.parentEntityId);
  return fetchRecord(entity.parentEntityId);
}
