import { COLLECTION_RULE_FIELD as FIELD, ENTITY_KIND, type EntityKindCode } from "$lib/api/generated/codes";

/** Entity targets for the backend's supported relationship rule fields. */
export const collectionRuleReferences: Record<string, { kind: EntityKindCode; label: string; useIds?: boolean }> = {
  [FIELD.tags]: { kind: ENTITY_KIND.tag, label: "Tags" },
  [FIELD.performers]: { kind: ENTITY_KIND.person, label: "People" },
  [FIELD.studio]: { kind: ENTITY_KIND.studio, label: "Studios" },
  [FIELD.videoSeriesId]: { kind: ENTITY_KIND.videoSeries, label: "Series", useIds: true },
};

/** Series rule values may be legacy titles or GUIDs. Only GUIDs are sent to the ID lookup endpoint. */
export function isSeriesReferenceId(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}
