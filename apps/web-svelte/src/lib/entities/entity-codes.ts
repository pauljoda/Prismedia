import {
  CAPABILITY_KIND,
  CREDIT_ROLE,
  ENTITY_DATE_TYPE,
  ENTITY_ENGAGEMENT_MODE,
  ENTITY_FILE_ROLE,
  ENTITY_KIND,
  ENTITY_KIND_CATEGORY,
  ENTITY_KIND_DEFINITIONS,
  ENTITY_KINDS_EXPANDING_RELATED_SEARCH_RESULTS,
  ENTITY_KINDS_ENUMERATING_IDENTIFY_CHILDREN,
  ENTITY_KINDS_IN_GLOBAL_SEARCH,
  ENTITY_KINDS_SUPPORTING_MANUAL_MANAGEMENT,
  MEDIA_IMAGE_KIND,
  METADATA_PATCH_FIELD,
  PLAYBACK_EVENT_KIND,
  RELATIONSHIP_CODE,
  type CapabilityKindCode,
  type CreditRoleCode,
  type EntityDateTypeCode,
  type EntityEngagementModeCode,
  type EntityFileRoleCode,
  type EntityKindCategoryCode,
  type EntityKindCode,
  type GlobalSearchEntityKindCode,
  type IdentifyContainerEntityKindCode,
  type ManuallyManageableEntityKindCode,
  type MediaImageKindCode,
  type MetadataPatchFieldCode,
  type PlaybackEventKindCode,
  type RelationshipCode,
} from "$lib/api/generated/codes";

// Stable code constants are generated from the backend registries (see
// scripts/gen-codes.mjs). This module re-exports them and owns only the frontend-specific
// concerns: tolerant lookups and generic interpretation of definition-owned route templates.
export {
  CAPABILITY_KIND,
  CREDIT_ROLE,
  ENTITY_DATE_TYPE,
  ENTITY_ENGAGEMENT_MODE,
  ENTITY_FILE_ROLE,
  ENTITY_KIND,
  ENTITY_KIND_CATEGORY,
  ENTITY_KIND_DEFINITIONS,
  ENTITY_KINDS_EXPANDING_RELATED_SEARCH_RESULTS,
  ENTITY_KINDS_ENUMERATING_IDENTIFY_CHILDREN,
  ENTITY_KINDS_IN_GLOBAL_SEARCH,
  ENTITY_KINDS_SUPPORTING_MANUAL_MANAGEMENT,
  MEDIA_IMAGE_KIND,
  METADATA_PATCH_FIELD,
  PLAYBACK_EVENT_KIND,
  RELATIONSHIP_CODE,
};
export type {
  CapabilityKindCode,
  CreditRoleCode,
  EntityDateTypeCode,
  EntityEngagementModeCode,
  EntityFileRoleCode,
  EntityKindCategoryCode,
  EntityKindCode,
  GlobalSearchEntityKindCode,
  IdentifyContainerEntityKindCode,
  ManuallyManageableEntityKindCode,
  MediaImageKindCode,
  MetadataPatchFieldCode,
  PlaybackEventKindCode,
  RelationshipCode,
};

export const ENTITY_KINDS = Object.values(ENTITY_KIND) as EntityKindCode[];

export interface EntityRouteContext {
  kind: EntityKindCode;
  id: string;
}

export function isEntityKindCode(value: string): value is EntityKindCode {
  return (ENTITY_KINDS as readonly string[]).includes(value);
}

/** Whether a kind belongs to the shared taxonomy/reference category. */
export function isTaxonomyEntityKind(kind: string): kind is EntityKindCode {
  return isEntityKindCode(kind) && ENTITY_KIND_DEFINITIONS[kind].category === ENTITY_KIND_CATEGORY.taxonomy;
}

export function labelForEntityKind(kind: string): string {
  if (isEntityKindCode(kind)) return ENTITY_KIND_DEFINITIONS[kind].groupLabel;
  return kind.replaceAll("-", " ").replace(/\b\w/g, (value) => value.toUpperCase());
}

export function displayNameForEntityKind(kind: string): string {
  if (isEntityKindCode(kind)) return ENTITY_KIND_DEFINITIONS[kind].displayName;
  return kind.replaceAll("-", " ").replace(/\b\w/g, (value) => value.toUpperCase());
}

/**
 * Compact shared name derived from the kind's semantic presentation concept. This preserves
 * concise labels such as Series, Artist, Album, and Track without a second per-screen kind map.
 */
export function shortDisplayNameForEntityKind(kind: string): string {
  if (!isEntityKindCode(kind)) return displayNameForEntityKind(kind);
  return ENTITY_KIND_DEFINITIONS[kind].presentation.icon
    .replaceAll("-", " ")
    .replace(/\b\w/g, (value) => value.toUpperCase());
}

export function resolveEntityHref(
  kind: string,
  id: string,
  parent?: EntityRouteContext,
): string | undefined {
  if (!isEntityKindCode(kind)) return undefined;
  const navigation = ENTITY_KIND_DEFINITIONS[kind].navigation;
  const template = navigation?.detailPathTemplate;
  if (!template) return undefined;
  if (navigation.requiredAncestorKind && !parent) {
    return undefined;
  }

  return template
    .replaceAll("{id}", id)
    .replaceAll("{parentId}", parent?.id ?? "");
}

export function resolveEntityBrowsePath(kind: string): string | undefined {
  return isEntityKindCode(kind)
    ? ENTITY_KIND_DEFINITIONS[kind].navigation?.browsePath
    : undefined;
}

export function isTopLevelEntityKind(kind: string): kind is EntityKindCode {
  return isEntityKindCode(kind) && (ENTITY_KIND_DEFINITIONS[kind].navigation?.isTopLevel ?? false);
}
