import type { EntityThumbnail } from "$lib/api/generated/model";
import type {
  CollectionCoverModeCode,
  CollectionItemSourceCode,
  CollectionModeCode,
} from "$lib/api/generated/codes";
import { ENTITY_KIND, ENTITY_KIND_DEFINITIONS } from "$lib/entities/entity-codes";

export type CollectionMode = CollectionModeCode;
export type CollectionCoverMode = CollectionCoverModeCode;
export type CollectionItemSource = CollectionItemSourceCode;

/**
 * Entity kind codes that can be stored as collection members. The generated Collection
 * definition projects the backend containment contract, so eligibility cannot drift by client.
 */
export const COLLECTION_ENTITY_TYPES = ENTITY_KIND_DEFINITIONS[ENTITY_KIND.collection].containableKinds;

export type CollectionEntityType = (typeof COLLECTION_ENTITY_TYPES)[number];

/** Narrows an arbitrary entity kind code to a {@link CollectionEntityType}. */
export function isCollectionEntityType(kind: string): kind is CollectionEntityType {
  return (COLLECTION_ENTITY_TYPES as readonly string[]).includes(kind);
}

export type CollectionOperator =
  | "equals"
  | "not_equals"
  | "contains"
  | "not_contains"
  | "greater_than"
  | "less_than"
  | "greater_equal"
  | "less_equal"
  | "between"
  | "in"
  | "not_in"
  | "is_null"
  | "is_not_null"
  | "is_true"
  | "is_false";

export type CollectionConditionValue = string | number | boolean | string[] | [number, number] | [string, string] | null;

export interface CollectionRuleCondition {
  type: "condition";
  entityTypes: CollectionEntityType[];
  field: string;
  operator: CollectionOperator;
  value: CollectionConditionValue;
}

export interface CollectionRuleGroup {
  type: "group";
  operator: "and" | "or" | "not";
  children: CollectionRuleNode[];
}

export type CollectionRuleNode = CollectionRuleCondition | CollectionRuleGroup;

export interface CollectionRuleFieldDef {
  field: string;
  label: string;
  fieldType: "text" | "number" | "boolean" | "date" | "relation" | "enum" | "library";
  entityTypes: CollectionEntityType[];
  enumValues?: string[];
  operators: CollectionOperator[];
}

export const COLLECTION_RULE_FIELDS: CollectionRuleFieldDef[] = [
  { field: "title", label: "Title", fieldType: "text", entityTypes: [], operators: ["contains", "not_contains", "equals", "not_equals"] },
  { field: "rating", label: "Rating", fieldType: "number", entityTypes: [], operators: ["equals", "not_equals", "greater_than", "less_than", "greater_equal", "less_equal", "between", "is_null", "is_not_null"] },
  { field: "date", label: "Date", fieldType: "date", entityTypes: [], operators: ["equals", "not_equals", "greater_than", "less_than", "between", "is_null", "is_not_null"] },
  { field: "organized", label: "Organized", fieldType: "boolean", entityTypes: [], operators: ["is_true", "is_false"] },
  { field: "isNsfw", label: "NSFW", fieldType: "boolean", entityTypes: [], operators: ["is_true", "is_false"] },
  { field: "tags", label: "Tags", fieldType: "relation", entityTypes: [], operators: ["in", "not_in"] },
  { field: "performers", label: "Performers", fieldType: "relation", entityTypes: [], operators: ["in", "not_in"] },
  { field: "studio", label: "Studio", fieldType: "relation", entityTypes: [], operators: ["in", "not_in", "is_null", "is_not_null"] },
  { field: "libraryRootId", label: "Library", fieldType: "library", entityTypes: [], operators: ["equals", "not_equals"] },
  { field: "createdAt", label: "Added Date", fieldType: "date", entityTypes: [], operators: ["greater_than", "less_than", "between"] },
  { field: "fileSize", label: "File Size", fieldType: "number", entityTypes: [ENTITY_KIND.video, ENTITY_KIND.image, ENTITY_KIND.audioTrack], operators: ["greater_than", "less_than", "between"] },
  { field: "duration", label: "Duration", fieldType: "number", entityTypes: [ENTITY_KIND.video, ENTITY_KIND.audioTrack], operators: ["greater_than", "less_than", "between", "is_null", "is_not_null"] },
  { field: "resolution", label: "Resolution", fieldType: "enum", entityTypes: [ENTITY_KIND.video], operators: ["in", "not_in"], enumValues: ["4K", "1080p", "720p", "480p"] },
  { field: "codec", label: "Codec", fieldType: "text", entityTypes: [ENTITY_KIND.video], operators: ["equals", "not_equals", "in", "not_in"] },
  { field: "interactive", label: "Interactive", fieldType: "boolean", entityTypes: [ENTITY_KIND.video], operators: ["is_true", "is_false"] },
  { field: "playCount", label: "Play Count", fieldType: "number", entityTypes: [ENTITY_KIND.video, ENTITY_KIND.audioTrack], operators: ["equals", "greater_than", "less_than", "greater_equal", "less_equal", "between"] },
  { field: "skipCount", label: "Skip Count", fieldType: "number", entityTypes: [ENTITY_KIND.video, ENTITY_KIND.audioTrack], operators: ["equals", "greater_than", "less_than", "greater_equal", "less_equal", "between"] },
  { field: "videoSeriesId", label: "Series", fieldType: "relation", entityTypes: [ENTITY_KIND.video], operators: ["equals", "in", "not_in"] },
  { field: "galleryType", label: "Gallery Type", fieldType: "enum", entityTypes: [ENTITY_KIND.gallery], operators: ["equals", "not_equals", "in"], enumValues: ["folder", "zip", "virtual"] },
  { field: "imageCount", label: "Image Count", fieldType: "number", entityTypes: [ENTITY_KIND.gallery], operators: ["greater_than", "less_than", "greater_equal", "less_equal", "between"] },
  { field: "width", label: "Width", fieldType: "number", entityTypes: [ENTITY_KIND.image], operators: ["greater_than", "less_than", "between"] },
  { field: "height", label: "Height", fieldType: "number", entityTypes: [ENTITY_KIND.image], operators: ["greater_than", "less_than", "between"] },
  { field: "format", label: "Format", fieldType: "text", entityTypes: [ENTITY_KIND.image], operators: ["equals", "not_equals", "in", "not_in"] },
  { field: "bitRate", label: "Bit Rate", fieldType: "number", entityTypes: [ENTITY_KIND.audioTrack], operators: ["greater_than", "less_than", "between"] },
  { field: "sampleRate", label: "Sample Rate", fieldType: "number", entityTypes: [ENTITY_KIND.audioTrack], operators: ["equals", "greater_than", "less_than", "between"] },
  { field: "channels", label: "Channels", fieldType: "number", entityTypes: [ENTITY_KIND.audioTrack], operators: ["equals", "greater_than", "less_than"] },
];

export const EMPTY_COLLECTION_RULE: CollectionRuleGroup = {
  type: "group",
  operator: "and",
  children: [],
};

export interface CollectionWriteRequest {
  title: string;
  description?: string | null;
  mode?: CollectionMode | null;
  ruleTreeJson?: string | null;
  coverMode?: CollectionCoverMode | null;
  coverItemId?: string | null;
  isNsfw?: boolean | null;
  isShared?: boolean | null;
}

export interface CollectionAddItemsRequest {
  items: { entityType: CollectionEntityType; entityId: string }[];
}

export interface CollectionItem {
  id: string;
  collectionId: string;
  entityType: CollectionEntityType;
  entityId: string;
  source: CollectionItemSource;
  sortOrder: number;
  addedAt: string;
  entity: EntityThumbnail | null;
}

export interface CollectionRulePreviewItem {
  entityType: CollectionEntityType;
  entityId: string;
  entity: EntityThumbnail;
}

export interface CollectionRulePreviewResponse {
  total: number;
  byType: Partial<Record<CollectionEntityType, number>>;
  sample: CollectionRulePreviewItem[];
}
