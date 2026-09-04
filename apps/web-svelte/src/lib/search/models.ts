import type { EntityThumbnail } from "$lib/api/generated/model";
import {
  ENTITY_KINDS_IN_GLOBAL_SEARCH,
  type GlobalSearchEntityKindCode,
} from "$lib/entities/entity-codes";

/**
 * Entity kinds surfaced by global search, in display order. Derived from the generated
 * kind codes so search can never carry a kind the backend does not define.
 */
export const ALL_SEARCH_KINDS = ENTITY_KINDS_IN_GLOBAL_SEARCH;

export type SearchEntityKind = GlobalSearchEntityKindCode;

export interface SearchRelatedEntity {
  id: string;
  kind: SearchEntityKind;
  title: string;
}

export interface SearchResultItem {
  /** Original list projection, retained for the same artwork and status presentation as library grids. */
  thumbnail?: EntityThumbnail;
  id: string;
  kind: SearchEntityKind;
  title: string;
  subtitle: string | null;
  imagePath: string | null;
  href: string;
  rating: number | null;
  score: number;
  meta: Record<string, string | number | boolean | string[] | null>;
  matchType?: "direct" | "related";
  relatedTo?: SearchRelatedEntity;
}

export interface SearchResultGroup {
  kind: SearchEntityKind;
  label: string;
  items: SearchResultItem[];
  total: number;
  hasMore: boolean;
}

export interface SearchResponse {
  query: string;
  groups: SearchResultGroup[];
  durationMs: number;
}
