import { fetchEntities, type EntityCard, type EntityListResponse } from "$lib/api/entities";
import type { ListEntitiesParams } from "$lib/api/generated/model";
import { ENTITY_KIND, labelForEntityKind } from "$lib/entities/entity-codes";
import { resolveEntityHref } from "$lib/entities/entity-routes";
import {
  ALL_SEARCH_KINDS,
  type SearchEntityKind,
  type SearchRelatedEntity,
  type SearchResponse,
  type SearchResultGroup,
  type SearchResultItem,
} from "./models";

const DEFAULT_DIRECT_LIMIT = 80;
const DEFAULT_RELATED_SOURCE_LIMIT = 4;
const DEFAULT_RELATED_LIMIT_PER_SOURCE = 30;

const RELATIONSHIP_SOURCE_KINDS = new Set<SearchEntityKind>([
  ENTITY_KIND.person,
  ENTITY_KIND.studio,
  ENTITY_KIND.tag,
]);

export type EntitySearchFetcher = (params?: ListEntitiesParams) => Promise<EntityListResponse>;

export interface EntitySearchOptions {
  query: string;
  hideNsfw?: boolean;
  kinds?: Iterable<SearchEntityKind>;
  directLimit?: number;
  relatedSourceLimit?: number;
  relatedLimitPerSource?: number;
  includeRelated?: boolean;
  fetcher?: EntitySearchFetcher;
}

export async function searchEntities(options: EntitySearchOptions): Promise<SearchResponse> {
  const trimmed = options.query.trim();
  const startedAt = performance.now();
  const fetcher = options.fetcher ?? fetchEntities;

  if (trimmed.length < 2) {
    return toSearchResponse(trimmed, startedAt, [], options.kinds);
  }

  const directResponse = await fetcher({
    query: trimmed,
    hideNsfw: options.hideNsfw,
    limit: options.directLimit ?? DEFAULT_DIRECT_LIMIT,
  });
  const directItems = directResponse.items
    .map((entity) => entityToSearchItem(entity, "direct"))
    .filter((item): item is SearchResultItem => Boolean(item));

  const relatedItems = options.includeRelated === false
    ? []
    : await fetchRelatedItems(directItems, options, fetcher);

  return toSearchResponse(trimmed, startedAt, [...directItems, ...relatedItems], options.kinds);
}

export function firstSearchResult(response: SearchResponse | null | undefined): SearchResultItem | null {
  return response?.groups.flatMap((group) => group.items)[0] ?? null;
}

export function flattenSearchResults(response: SearchResponse | null | undefined): SearchResultItem[] {
  return response?.groups.flatMap((group) => group.items) ?? [];
}

export function entityToSearchItem(
  entity: EntityCard,
  matchType: SearchResultItem["matchType"] = "direct",
  relatedTo?: SearchRelatedEntity,
): SearchResultItem | null {
  const kind = toSearchKind(entity.kind);
  const href = resolveEntityHref(entity.kind, entity.id);
  if (!kind || !href) return null;

  return {
    href,
    id: entity.id,
    imagePath: entity.coverUrl ?? null,
    kind,
    matchType,
    meta: {},
    rating: typeof entity.rating === "number" ? entity.rating : null,
    relatedTo,
    score: matchType === "direct" ? 2 : 1,
    subtitle: relatedTo
      ? `${labelForEntityKind(entity.kind)} · Related to ${relatedTo.title}`
      : labelForEntityKind(entity.kind),
    title: entity.title,
  };
}

function toSearchResponse(
  term: string,
  startedAt: number,
  items: SearchResultItem[],
  kinds?: Iterable<SearchEntityKind>,
): SearchResponse {
  const allowedKinds = new Set(kinds ?? ALL_SEARCH_KINDS);
  const groups = new Map<SearchEntityKind, SearchResultItem[]>();

  for (const item of items) {
    if (!allowedKinds.has(item.kind)) continue;
    groups.set(item.kind, [...(groups.get(item.kind) ?? []), item]);
  }

  return {
    durationMs: Math.max(0, Math.round(performance.now() - startedAt)),
    groups: [...groups.entries()].map(([kind, groupItems]) => toResultGroup(kind, groupItems)),
    query: term,
  };
}

function toResultGroup(kind: SearchEntityKind, items: SearchResultItem[]): SearchResultGroup {
  return {
    hasMore: false,
    items,
    kind,
    label: labelForEntityKind(kind),
    total: items.length,
  };
}

async function fetchRelatedItems(
  directItems: SearchResultItem[],
  options: EntitySearchOptions,
  fetcher: EntitySearchFetcher,
): Promise<SearchResultItem[]> {
  const sources = directItems
    .filter((item) => RELATIONSHIP_SOURCE_KINDS.has(item.kind))
    .slice(0, options.relatedSourceLimit ?? DEFAULT_RELATED_SOURCE_LIMIT);
  if (sources.length === 0) return [];

  const seen = new Set(directItems.map((item) => item.id));
  const batches = await Promise.allSettled(
    sources.map(async (source) => {
      const response = await fetcher({
        referencedBy: source.id,
        hideNsfw: options.hideNsfw,
        limit: options.relatedLimitPerSource ?? DEFAULT_RELATED_LIMIT_PER_SOURCE,
      });
      return response.items
        .map((entity) => entityToSearchItem(entity, "related", {
          id: source.id,
          kind: source.kind,
          title: source.title,
        }))
        .filter((item): item is SearchResultItem => Boolean(item));
    }),
  );

  const related: SearchResultItem[] = [];
  for (const batch of batches) {
    if (batch.status !== "fulfilled") continue;
    for (const item of batch.value) {
      if (seen.has(item.id)) continue;
      seen.add(item.id);
      related.push(item);
    }
  }

  return related;
}

function toSearchKind(kind: string): SearchEntityKind | null {
  return (ALL_SEARCH_KINDS as readonly string[]).includes(kind) ? (kind as SearchEntityKind) : null;
}
