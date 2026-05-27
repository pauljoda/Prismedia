export type CollectionMode = "manual" | "dynamic" | "hybrid";
export type CollectionCoverMode = "mosaic" | "custom" | "item";
export type CollectionEntityType = "video" | "gallery" | "image" | "book" | "audio-track";
export type CollectionItemSource = "manual" | "dynamic";

// ─── Collection Rule Tree ──────────────────────────────────────

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

export type CollectionConditionValue =
  | string
  | number
  | boolean
  | string[]
  | [number, number]
  | null;

export interface CollectionRuleCondition {
  type: "condition";
  /** Entity types this condition applies to. Empty array = all types. */
  entityTypes: CollectionEntityType[];
  field: string;
  operator: CollectionOperator;
  value: CollectionConditionValue;
}

export interface CollectionRuleGroup {
  type: "group";
  operator: "and" | "or" | "not";
  children: (CollectionRuleCondition | CollectionRuleGroup)[];
}

export type CollectionRuleNode = CollectionRuleCondition | CollectionRuleGroup;

export type CollectionRuleFieldType =
  | "text"
  | "number"
  | "boolean"
  | "date"
  | "relation"
  | "enum";

export interface CollectionRuleFieldDef {
  field: string;
  label: string;
  fieldType: CollectionRuleFieldType;
  /** Entity types this field is available for. Empty = all types. */
  entityTypes: CollectionEntityType[];
  /** For enum fields, the available values. */
  enumValues?: string[];
  /** Which operators are valid for this field type. */
  operators: CollectionOperator[];
}

export const COLLECTION_RULE_FIELDS: CollectionRuleFieldDef[] = [
  // Universal fields (apply to all entity types)
  { field: "title", label: "Title", fieldType: "text", entityTypes: [], operators: ["contains", "not_contains", "equals", "not_equals"] },
  { field: "rating", label: "Rating", fieldType: "number", entityTypes: [], operators: ["equals", "not_equals", "greater_than", "less_than", "greater_equal", "less_equal", "between", "is_null", "is_not_null"] },
  { field: "date", label: "Date", fieldType: "date", entityTypes: [], operators: ["equals", "not_equals", "greater_than", "less_than", "between", "is_null", "is_not_null"] },
  { field: "organized", label: "Organized", fieldType: "boolean", entityTypes: [], operators: ["is_true", "is_false"] },
  { field: "isNsfw", label: "NSFW", fieldType: "boolean", entityTypes: [], operators: ["is_true", "is_false"] },
  { field: "tags", label: "Tags", fieldType: "relation", entityTypes: [], operators: ["in", "not_in"] },
  { field: "performers", label: "Performers", fieldType: "relation", entityTypes: [], operators: ["in", "not_in"] },
  { field: "studio", label: "Studio", fieldType: "relation", entityTypes: [], operators: ["in", "not_in", "is_null", "is_not_null"] },
  { field: "createdAt", label: "Added Date", fieldType: "date", entityTypes: [], operators: ["greater_than", "less_than", "between"] },
  { field: "fileSize", label: "File Size", fieldType: "number", entityTypes: ["video", "image", "audio-track"], operators: ["greater_than", "less_than", "between"] },

  // Video-specific
  { field: "duration", label: "Duration", fieldType: "number", entityTypes: ["video", "audio-track"], operators: ["greater_than", "less_than", "between", "is_null", "is_not_null"] },
  { field: "resolution", label: "Resolution", fieldType: "enum", entityTypes: ["video"], operators: ["in", "not_in"], enumValues: ["4K", "1080p", "720p", "480p"] },
  { field: "codec", label: "Codec", fieldType: "text", entityTypes: ["video"], operators: ["equals", "not_equals", "in", "not_in"] },
  { field: "interactive", label: "Interactive", fieldType: "boolean", entityTypes: ["video"], operators: ["is_true", "is_false"] },
  { field: "playCount", label: "Play Count", fieldType: "number", entityTypes: ["video", "audio-track"], operators: ["equals", "greater_than", "less_than", "greater_equal", "less_equal", "between"] },
  { field: "videoSeriesId", label: "Series", fieldType: "relation", entityTypes: ["video"], operators: ["equals", "in", "not_in"] },

  // Gallery-specific
  { field: "galleryType", label: "Gallery Type", fieldType: "enum", entityTypes: ["gallery"], operators: ["equals", "not_equals", "in"], enumValues: ["folder", "zip", "virtual"] },
  { field: "imageCount", label: "Image Count", fieldType: "number", entityTypes: ["gallery"], operators: ["greater_than", "less_than", "greater_equal", "less_equal", "between"] },

  // Image-specific
  { field: "width", label: "Width", fieldType: "number", entityTypes: ["image"], operators: ["greater_than", "less_than", "between"] },
  { field: "height", label: "Height", fieldType: "number", entityTypes: ["image"], operators: ["greater_than", "less_than", "between"] },
  { field: "format", label: "Format", fieldType: "text", entityTypes: ["image"], operators: ["equals", "not_equals", "in", "not_in"] },

  // Audio-specific
  { field: "bitRate", label: "Bit Rate", fieldType: "number", entityTypes: ["audio-track"], operators: ["greater_than", "less_than", "between"] },
  { field: "channels", label: "Channels", fieldType: "number", entityTypes: ["audio-track"], operators: ["equals", "greater_than", "less_than"] },
];

// ─── Collection DTOs ───────────────────────────────────────────

export interface CollectionListItemDto {
  id: string;
  name: string;
  description: string | null;
  mode: CollectionMode;
  itemCount: number;
  coverMode: CollectionCoverMode;
  coverImagePath: string | null;
  slideshowDurationSeconds: number;
  slideshowAutoAdvance: boolean;
  isNsfw: boolean;
  /** Breakdown of items by entity type. */
  typeCounts: Record<CollectionEntityType, number>;
  lastRefreshedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CollectionDetailDto extends CollectionListItemDto {
  ruleTree: CollectionRuleGroup | null;
  coverItemId: string | null;
  coverItemType: CollectionEntityType | null;
}

export interface CollectionItemDto {
  id: string;
  collectionId: string;
  entityType: CollectionEntityType;
  entityId: string;
  source: CollectionItemSource;
  sortOrder: number;
  addedAt: string;
  /**
   * Polymorphic entity embed — exactly one of these is populated based on
   * entityType. The shape matches the respective list-item endpoint response
   * (VideoListItem, GalleryListItemDto, ImageListItemDto, AudioTrackListItemDto).
   */
  entity: Record<string, unknown> | null;
}

export interface CollectionCreateDto {
  name: string;
  description?: string;
  mode?: CollectionMode;
  ruleTree?: CollectionRuleGroup;
  slideshowDurationSeconds?: number;
  slideshowAutoAdvance?: boolean;
  isNsfw?: boolean;
}

export interface CollectionPatchDto {
  name?: string;
  description?: string | null;
  mode?: CollectionMode;
  ruleTree?: CollectionRuleGroup | null;
  coverMode?: CollectionCoverMode;
  coverItemId?: string | null;
  coverItemType?: CollectionEntityType | null;
  slideshowDurationSeconds?: number;
  slideshowAutoAdvance?: boolean;
  isNsfw?: boolean;
}

export interface CollectionAddItemsDto {
  items: { entityType: CollectionEntityType; entityId: string }[];
}

export interface CollectionRemoveItemsDto {
  itemIds: string[];
}

export interface CollectionReorderDto {
  /** Ordered list of item IDs — position in array becomes sortOrder. */
  itemIds: string[];
}

export interface CollectionRulePreviewDto {
  total: number;
  byType: Record<CollectionEntityType, number>;
  sample: CollectionItemDto[];
}

export interface PlaylistSessionDto {
  collectionId: string | null;
  collectionName: string;
  items: CollectionItemDto[];
  playOrder: number[];
  orderPosition: number;
  shuffle: boolean;
  loop: boolean;
  slideshowDurationSeconds: number;
  updatedAt: string;
}

export interface PlaylistSessionWriteDto {
  collectionId: string | null;
  collectionName: string;
  items: CollectionItemDto[];
  playOrder: number[];
  orderPosition: number;
  shuffle: boolean;
  loop: boolean;
  slideshowDurationSeconds: number;
}
