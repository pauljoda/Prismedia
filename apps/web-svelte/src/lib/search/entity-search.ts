import { fetchEntities, type EntityCard, type EntityListResponse } from "$lib/api/entities";
import type { ListEntitiesParams } from "$lib/api/generated/model";
import {
  ENTITY_KINDS_EXPANDING_RELATED_SEARCH_RESULTS,
  labelForEntityKind,
} from "$lib/entities/entity-codes";
import { resolveEntityHref } from "$lib/entities/entity-routes";
import {
  ALL_SEARCH_KINDS,
  type SearchEntityKind,
  type SearchRelatedEntity,
  type SearchResponse,
  type SearchResultGroup,
  type SearchResultItem,
  type SearchContinuation,
  type SearchPageRequest,
} from "./models";

const DEFAULT_DIRECT_LIMIT = 80;
const DEFAULT_RELATED_SOURCE_LIMIT = 4;
const DEFAULT_RELATED_LIMIT_PER_SOURCE = 30;

const RELATIONSHIP_SOURCE_KINDS = new Set<SearchEntityKind>(
  ENTITY_KINDS_EXPANDING_RELATED_SEARCH_RESULTS,
);

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

  const continuation: SearchContinuation = {
    requests: [], expandedSourceIds: [], kinds: [...(options.kinds ?? ALL_SEARCH_KINDS)],
    includeRelated: options.includeRelated !== false && options.relatedSourceLimit !== 0,
    relatedLimit: options.relatedLimitPerSource ?? DEFAULT_RELATED_LIMIT_PER_SOURCE,
    batchSize: Math.max(1, options.relatedSourceLimit ?? DEFAULT_RELATED_SOURCE_LIMIT),
  };
  if (directResponse.nextCursor) continuation.requests.push({ params: {
    query: trimmed, hideNsfw: options.hideNsfw, limit: options.directLimit ?? DEFAULT_DIRECT_LIMIT,
    cursor: directResponse.nextCursor,
  } });
  queueRelatedSources(directItems, options.hideNsfw, continuation);
  const initial = { ...toSearchResponse(trimmed, startedAt, directItems, options.kinds), continuation };
  const relatedRequests = continuation.requests.filter(request => request.relatedTo).slice(0, continuation.batchSize);
  return executeSearchRequests(initial, relatedRequests, fetcher);
}

/** Fetches one bounded batch of pending direct and related pages, retaining retryable failures. */
export async function loadMoreSearchResults(previous: SearchResponse, fetcher: EntitySearchFetcher = fetchEntities): Promise<SearchResponse> {
  if (!previous.continuation) return previous;
  return executeSearchRequests(previous, previous.continuation.requests.slice(0, previous.continuation.batchSize), fetcher);
}

/** Adds each relationship source once, including sources beyond the initial request budget. */
function queueRelatedSources(items: SearchResultItem[], hideNsfw: boolean | undefined, continuation: SearchContinuation): void {
  if (!continuation.includeRelated) return;
  const seen = new Set(continuation.expandedSourceIds);
  for (const source of items) {
    if (!RELATIONSHIP_SOURCE_KINDS.has(source.kind) || seen.has(source.id)) continue;
    seen.add(source.id);
    continuation.expandedSourceIds.push(source.id);
    continuation.requests.push({
      params: { referencedBy: source.id, hideNsfw, limit: continuation.relatedLimit },
      relatedTo: { id: source.id, kind: source.kind, title: source.title },
    });
  }
}

async function executeSearchRequests(previous: SearchResponse, selected: SearchPageRequest[], fetcher: EntitySearchFetcher): Promise<SearchResponse> {
  const startedAt = performance.now();
  const current = previous.continuation!;
  const continuation: SearchContinuation = {
    ...current,
    requests: current.requests.filter(request => !selected.includes(request)),
    expandedSourceIds: [...current.expandedSourceIds],
  };
  const batches = await Promise.allSettled(selected.map(async request => {
    const response = await fetcher(request.params);
    if (response.nextCursor && response.nextCursor === request.params.cursor) throw new Error("Search cursor did not advance");
    return response;
  }));
  const merged = new Map(flattenSearchResults(previous).map(item => [item.id, item]));
  for (const [index, batch] of batches.entries()) {
    const request = selected[index];
    if (batch.status === "rejected") {
      continuation.requests.push({ ...request, failed: true });
      continue;
    }
    const items = batch.value.items.map(entity => entityToSearchItem(entity, request.relatedTo ? "related" : "direct", request.relatedTo))
      .filter((item): item is SearchResultItem => Boolean(item));
    for (const item of items) {
      const existing = merged.get(item.id);
      if (!existing || existing.matchType === "related" && item.matchType === "direct") merged.set(item.id, item);
    }
    if (batch.value.nextCursor) {
      const next = { params: { ...request.params, cursor: batch.value.nextCursor }, relatedTo: request.relatedTo };
      if (request.relatedTo) continuation.requests.push(next);
      else continuation.requests.unshift(next);
    }
    if (!request.relatedTo) queueRelatedSources(items, request.params.hideNsfw, continuation);
  }
  const items = [...merged.values()].sort((a, b) => b.score - a.score);
  const response = toSearchResponse(previous.query, startedAt, items, current.kinds);
  return {
    ...response,
    durationMs: previous.durationMs + response.durationMs,
    continuation: continuation.requests.length ? continuation : undefined,
    partialFailure: continuation.requests.some(request => request.failed),
  };
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
    thumbnail: entity,
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


function toSearchKind(kind: string): SearchEntityKind | null {
  return (ALL_SEARCH_KINDS as readonly string[]).includes(kind) ? (kind as SearchEntityKind) : null;
}
