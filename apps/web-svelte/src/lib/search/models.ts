import type { EntityThumbnail, ListEntitiesParams } from "$lib/api/generated/model";
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
  /** Remaining Entity list pages, fetched only after an explicit continuation. */
  continuation?: SearchContinuation;
  partialFailure?: boolean;
}

/** A pending direct or relationship-based Entity list request. */
export interface SearchPageRequest {
  params: ListEntitiesParams;
  relatedTo?: SearchRelatedEntity;
  failed?: boolean;
}

/** Client-side search traversal state. Cursors remain opaque API values. */
export interface SearchContinuation {
  requests: SearchPageRequest[];
  expandedSourceIds: string[];
  kinds: SearchEntityKind[];
  includeRelated: boolean;
  relatedLimit: number;
  batchSize: number;
}
