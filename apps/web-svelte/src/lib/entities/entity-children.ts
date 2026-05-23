import type { EntityGroup } from "$lib/api/generated/model";
import type { EntityKindCode, RelationshipCode } from "./entity-codes";

export interface EntityChildGroupSource {
  childrenByKind?: EntityGroup[] | null;
}

export function getChildIds(
  entity: EntityChildGroupSource | null | undefined,
  kind: EntityKindCode,
): string[] {
  const group = entity?.childrenByKind?.find((candidate) => candidate.kind === kind);
  return group?.entities.map((child) => child.id) ?? [];
}

export function getAllChildIds(entity: EntityChildGroupSource | null | undefined): string[] {
  return (entity?.childrenByKind ?? []).flatMap((group) => group.entities.map((child) => child.id));
}

export interface EntityRelationshipGroupSource {
  relationships?: EntityGroup[] | null;
}

export function getRelationshipIds(
  entity: EntityRelationshipGroupSource | null | undefined,
  code: RelationshipCode | string,
  kind?: EntityKindCode,
): string[] {
  return (entity?.relationships ?? [])
    .filter((group) => group.code === code && (!kind || group.kind === kind))
    .flatMap((group) => group.entities.map((relationship) => relationship.id));
}

export function getRelationships(
  entity: EntityRelationshipGroupSource | null | undefined,
  kind: EntityKindCode,
): EntityGroup[] {
  return (entity?.relationships ?? []).filter((group) => group.kind === kind);
}
