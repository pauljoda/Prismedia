import { ENTITY_KIND } from "$lib/entities/entity-codes";

/**
 * Entity kinds surfaced by global search, in display order. Derived from the generated
 * kind codes so search can never carry a kind the backend does not define.
 */
export const ALL_SEARCH_KINDS = [
  ENTITY_KIND.movie,
  ENTITY_KIND.videoSeries,
  ENTITY_KIND.video,
  ENTITY_KIND.person,
  ENTITY_KIND.studio,
  ENTITY_KIND.tag,
  ENTITY_KIND.gallery,
  ENTITY_KIND.book,
  ENTITY_KIND.image,
  ENTITY_KIND.collection,
  ENTITY_KIND.audioLibrary,
  ENTITY_KIND.audioTrack,
] as const;

export type SearchEntityKind = (typeof ALL_SEARCH_KINDS)[number];

export interface SearchRelatedEntity {
  id: string;
  kind: SearchEntityKind;
  title: string;
}

export interface SearchResultItem {
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
