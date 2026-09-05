import type { EntityThumbnail } from "$lib/api/generated/model";
import type {
  CollectionCoverModeCode,
  CollectionItemSourceCode,
  CollectionModeCode,
  CollectionRuleFieldCode,
  CollectionRuleOperatorCode,
} from "$lib/api/generated/codes";
import { COLLECTION_RULE_FIELD as FIELD, COLLECTION_RULE_OPERATOR as OP, COLLECTION_RULE_TARGET_KINDS, GALLERY_TYPE, MEDIA_RESOLUTION_TIERS } from "$lib/api/generated/codes";
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

export type CollectionOperator = CollectionRuleOperatorCode;

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
  field: CollectionRuleFieldCode;
  label: string;
  fieldType: "text" | "number" | "boolean" | "date" | "relation" | "enum" | "library";
  entityTypes: CollectionEntityType[];
  enumValues?: string[];
  operators: CollectionOperator[];
}

const fieldDefinitions: Omit<CollectionRuleFieldDef, "entityTypes">[] = [
  { field: FIELD.title, label: "Title", fieldType: "text", operators: [OP.contains, OP.notContains, OP.equals, OP.notEquals] },
  { field: FIELD.rating, label: "Rating", fieldType: "number", operators: [OP.equals, OP.notEquals, OP.greaterThan, OP.lessThan, OP.greaterEqual, OP.lessEqual, OP.between, OP.isNull, OP.isNotNull] },
  { field: FIELD.date, label: "Date", fieldType: "date", operators: [OP.equals, OP.notEquals, OP.greaterThan, OP.lessThan, OP.between, OP.isNull, OP.isNotNull] },
  { field: FIELD.organized, label: "Organized", fieldType: "boolean", operators: [OP.isTrue, OP.isFalse] },
  { field: FIELD.isNsfw, label: "NSFW", fieldType: "boolean", operators: [OP.isTrue, OP.isFalse] },
  { field: FIELD.tags, label: "Tags", fieldType: "relation", operators: [OP.in, OP.notIn] },
  { field: FIELD.performers, label: "Performers", fieldType: "relation", operators: [OP.in, OP.notIn] },
  { field: FIELD.studio, label: "Studio", fieldType: "relation", operators: [OP.in, OP.notIn, OP.isNull, OP.isNotNull] },
  { field: FIELD.libraryRootId, label: "Library", fieldType: "library", operators: [OP.equals, OP.notEquals] },
  { field: FIELD.createdAt, label: "Added Date", fieldType: "date", operators: [OP.greaterThan, OP.lessThan, OP.between] },
  { field: FIELD.fileSize, label: "File Size", fieldType: "number", operators: [OP.greaterThan, OP.lessThan, OP.between] },
  { field: FIELD.duration, label: "Duration", fieldType: "number", operators: [OP.greaterThan, OP.lessThan, OP.between, OP.isNull, OP.isNotNull] },
  { field: FIELD.resolution, label: "Resolution", fieldType: "enum", operators: [OP.in, OP.notIn], enumValues: MEDIA_RESOLUTION_TIERS.map(tier => tier.code) },
  { field: FIELD.codec, label: "Codec", fieldType: "text", operators: [OP.equals, OP.notEquals, OP.in, OP.notIn] },
  { field: FIELD.interactive, label: "Interactive", fieldType: "boolean", operators: [OP.isTrue, OP.isFalse] },
  { field: FIELD.accessCount, label: "Play Count", fieldType: "number", operators: [OP.equals, OP.greaterThan, OP.lessThan, OP.greaterEqual, OP.lessEqual, OP.between] },
  { field: FIELD.skipCount, label: "Skip Count", fieldType: "number", operators: [OP.equals, OP.greaterThan, OP.lessThan, OP.greaterEqual, OP.lessEqual, OP.between] },
  { field: FIELD.videoSeriesId, label: "Series", fieldType: "relation", operators: [OP.equals, OP.in, OP.notIn] },
  { field: FIELD.galleryType, label: "Gallery Type", fieldType: "enum", operators: [OP.equals, OP.notEquals, OP.in], enumValues: [GALLERY_TYPE.folder, GALLERY_TYPE.zip, GALLERY_TYPE.virtual] },
  { field: FIELD.imageCount, label: "Image Count", fieldType: "number", operators: [OP.greaterThan, OP.lessThan, OP.greaterEqual, OP.lessEqual, OP.between] },
  { field: FIELD.width, label: "Width", fieldType: "number", operators: [OP.greaterThan, OP.lessThan, OP.between] },
  { field: FIELD.height, label: "Height", fieldType: "number", operators: [OP.greaterThan, OP.lessThan, OP.between] },
  { field: FIELD.format, label: "Format", fieldType: "text", operators: [OP.equals, OP.notEquals, OP.in, OP.notIn] },
  { field: FIELD.bitRate, label: "Bit Rate", fieldType: "number", operators: [OP.greaterThan, OP.lessThan, OP.between] },
  { field: FIELD.sampleRate, label: "Sample Rate", fieldType: "number", operators: [OP.equals, OP.greaterThan, OP.lessThan, OP.between] },
  { field: FIELD.channels, label: "Channels", fieldType: "number", operators: [OP.equals, OP.greaterThan, OP.lessThan] },
];

/** Field applicability is generated from the same policy used by the rule engine. */
export const COLLECTION_RULE_FIELDS: CollectionRuleFieldDef[] = fieldDefinitions.map(field => ({
  ...field,
  entityTypes: COLLECTION_RULE_TARGET_KINDS[field.field].filter(isCollectionEntityType),
}));

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
