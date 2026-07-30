import { fetchEntity } from "$lib/api/entities";
import { ENTITY_KIND_DEFINITIONS, isEntityKindCode, resolveEntityHref } from "./entity-codes";

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
  if (!isEntityKindCode(entity.kind)) return null;
  const navigation = ENTITY_KIND_DEFINITIONS[entity.kind].navigation;
  if (!navigation) return null;

  if (!navigation.detailPathTemplate) {
    const parent = await parentRecord(entity, fetchRecord, seen);
    return parent ? resolveEntityHrefForRecord(parent, fetchRecord, seen) : null;
  }

  if (!navigation.requiredAncestorKind) {
    return resolveEntityHref(entity.kind, entity.id) ?? null;
  }

  const ancestor = await ancestorRecord(
    entity,
    navigation.requiredAncestorKind,
    fetchRecord,
    seen,
  );
  return ancestor
    ? resolveEntityHref(entity.kind, entity.id, {
        kind: navigation.requiredAncestorKind,
        id: ancestor.id,
      }) ?? null
    : null;
}

async function ancestorRecord(
  entity: EntityRouteRecord,
  requiredKind: string,
  fetchRecord: EntityRouteFetcher,
  seen: Set<string>,
): Promise<EntityRouteRecord | null> {
  let current = entity;
  while (true) {
    const parent = await parentRecord(current, fetchRecord, seen);
    if (!parent) return null;
    if (parent.kind === requiredKind) return parent;
    current = parent;
  }
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
