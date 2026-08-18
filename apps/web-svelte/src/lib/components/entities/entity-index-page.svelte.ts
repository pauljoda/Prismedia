import {
  fetchEntities,
  type EntityCard,
  type EntityListResponse,
} from "$lib/api/entities";
import type { ListEntitiesParams } from "$lib/api/generated/model";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import { entityCardToThumbnailCard, type EntityGridServerQuery } from "$lib/entities/entity-grid";
import { resolveEntityHref } from "$lib/entities/entity-routes";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

const DEFAULT_ENTITY_PAGE_SIZE = 100;
const INITIAL_PAGE_CACHE_TTL_MS = 30_000;
const INITIAL_PAGE_CACHE_LIMIT = 12;

interface EntityInitialPageCacheEntry {
  expiresAt: number;
  response: EntityListResponse;
}

const initialPageCache = new Map<string, EntityInitialPageCacheEntry>();
const initialPageRequests = new Map<string, Promise<EntityListResponse>>();
let initialPageCacheGeneration = 0;

function initialPageCacheKey(params: ListEntitiesParams): string {
  return JSON.stringify(
    Object.entries(params)
      .filter(([, value]) => value !== undefined)
      .sort(([left], [right]) => left.localeCompare(right)),
  );
}

function readCachedInitialPage(key: string): EntityListResponse | null {
  const entry = initialPageCache.get(key);
  if (!entry) return null;
  if (entry.expiresAt <= Date.now()) {
    initialPageCache.delete(key);
    return null;
  }

  initialPageCache.delete(key);
  initialPageCache.set(key, entry);
  return entry.response;
}

function cacheInitialPage(key: string, response: EntityListResponse): void {
  initialPageCache.set(key, {
    expiresAt: Date.now() + INITIAL_PAGE_CACHE_TTL_MS,
    response,
  });
  while (initialPageCache.size > INITIAL_PAGE_CACHE_LIMIT) {
    const oldest = initialPageCache.keys().next().value;
    if (oldest === undefined) break;
    initialPageCache.delete(oldest);
  }
}

function fetchInitialPage(key: string, params: ListEntitiesParams): Promise<EntityListResponse> {
  const existing = initialPageRequests.get(key);
  if (existing) return existing;

  const generation = initialPageCacheGeneration;
  let request: Promise<EntityListResponse>;
  request = fetchEntities(params)
    .then((response) => {
      if (generation === initialPageCacheGeneration) cacheInitialPage(key, response);
      return response;
    })
    .finally(() => {
      if (initialPageRequests.get(key) === request) initialPageRequests.delete(key);
    });
  initialPageRequests.set(key, request);
  return request;
}

/** Clears the short-lived navigation cache after a mutation or in isolated tests. */
export function clearEntityIndexPageCache(): void {
  initialPageCacheGeneration += 1;
  initialPageCache.clear();
  initialPageRequests.clear();
}

function requireTotalCount(value: number | string): number {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  throw new Error("Entity list totalCount must be a number.");
}

/** Shallow structural equality for the server query, treating it as a flat bag. */
function sameServerQuery(a: EntityGridServerQuery, b: EntityGridServerQuery): boolean {
  const keys = new Set([...Object.keys(a), ...Object.keys(b)]) as Set<keyof EntityGridServerQuery>;
  for (const key of keys) {
    if (a[key] !== b[key]) return false;
  }
  return true;
}

export type EntityIndexLoadState = "loading" | "ready" | "error";

export interface EntityIndexPageStateOptions {
  getKind: () => string;
  getHideNsfw: () => boolean;
  resolveHref?: (item: EntityCard) => string | undefined;
  /**
   * Server query parameters that always apply to this index, regardless of the
   * grid's filter controls. Used by constrained sub-views (e.g. Comics/eBooks
   * lock book type/format). Spread after the user's {@link serverQuery} so the lock
   * always wins.
   */
  lockedServerQuery?: Partial<EntityGridServerQuery>;
}

export class EntityIndexPageState {
  errorMessage = $state<string | null>(null);
  items = $state.raw<EntityCard[]>([]);
  loadMoreError = $state<string | null>(null);
  loadState = $state<EntityIndexLoadState>("loading");
  loadingMore = $state(false);
  nextCursor = $state<string | null>(null);
  pageSize = $state(DEFAULT_ENTITY_PAGE_SIZE);
  query = $state("");
  totalCount = $state(0);
  /**
   * Server-resolvable sort and filter parameters mirrored from the grid
   * controls. Changing them re-fetches from the first page so the sort and
   * filters apply across the entire library rather than the loaded page.
   */
  serverQuery = $state.raw<EntityGridServerQuery>({});

  cards: EntityThumbnailCard[] = $derived.by(() =>
    this.items.map((item) => entityCardToThumbnailCard(item, this.hrefFor(item))),
  );

  readonly #options: EntityIndexPageStateOptions;
  #searchTimer: ReturnType<typeof setTimeout> | null = null;
  #searchAbort: AbortController | null = null;
  #loadStarted = false;

  constructor(options: EntityIndexPageStateOptions) {
    this.#options = options;
  }

  /**
   * Loads the first page only if nothing has kicked off a load yet. The grid's
   * initial request-change event usually triggers {@link loadInitial} before the
   * page's own mount hook runs; this keeps the mount fallback from dispatching a
   * duplicate of the same query.
   */
  async ensureLoaded() {
    if (this.#loadStarted) return;
    await this.loadInitial();
  }

  async loadInitial() {
    this.#loadStarted = true;
    this.#searchAbort?.abort();
    this.#searchAbort = new AbortController();
    const signal = this.#searchAbort.signal;

    const params: ListEntitiesParams = {
      kind: this.#options.getKind(),
      query: this.query || undefined,
      hideNsfw: this.#options.getHideNsfw(),
      limit: this.pageSize,
      ...this.serverQuery,
      ...this.#options.lockedServerQuery,
    };
    const cacheKey = initialPageCacheKey(params);
    const cached = readCachedInitialPage(cacheKey);
    if (cached) {
      this.#applyInitialResponse(cached);
      return;
    }

    this.loadState = "loading";
    this.errorMessage = null;
    this.loadMoreError = null;
    this.items = [];
    this.nextCursor = null;
    this.totalCount = 0;

    try {
      // Do not cancel a shared transport when one route instance leaves. It can still populate the
      // navigation cache for Back, while this instance ignores the result through its local signal.
      const response = await fetchInitialPage(cacheKey, params);
      if (signal.aborted) return;
      this.#applyInitialResponse(response);
    } catch (err) {
      if (signal.aborted || (err instanceof DOMException && err.name === "AbortError")) return;
      this.errorMessage = err instanceof Error ? err.message : String(err);
      this.loadState = "error";
    }
  }

  #applyInitialResponse(response: EntityListResponse): void {
    this.items = response.items;
    this.nextCursor = response.nextCursor;
    this.totalCount = requireTotalCount(response.totalCount);
    this.errorMessage = null;
    this.loadMoreError = null;
    this.loadState = "ready";
  }

  async loadMore() {
    if (!this.nextCursor || this.loadingMore) return;
    this.loadingMore = true;
    this.loadMoreError = null;

    try {
      const response = await fetchEntities({
        kind: this.#options.getKind(),
        query: this.query || undefined,
        cursor: this.nextCursor,
        hideNsfw: this.#options.getHideNsfw(),
        limit: this.pageSize,
        ...this.serverQuery,
        ...this.#options.lockedServerQuery,
      });
      this.items = [...this.items, ...response.items];
      this.nextCursor = response.nextCursor;
      this.totalCount = requireTotalCount(response.totalCount);
    } catch (err) {
      this.loadMoreError = err instanceof Error ? err.message : String(err);
    } finally {
      this.loadingMore = false;
    }
  }

  setQuery(value: string) {
    const trimmed = value.trim();
    if (trimmed === this.query) return;
    this.query = trimmed;
    if (this.#searchTimer) clearTimeout(this.#searchTimer);
    this.#searchTimer = setTimeout(() => {
      this.#searchTimer = null;
      void this.loadInitial();
    }, 300);
  }

  /**
   * Applies a new server-resolvable sort/filter query. Re-fetches from the first
   * page only when the effective parameters actually change so unrelated grid
   * interactions (scale, view mode) do not trigger redundant network loads.
   */
  setServerQuery(next: EntityGridServerQuery) {
    if (sameServerQuery(this.serverQuery, next)) return;
    this.serverQuery = next;
    void this.loadInitial();
  }

  #defaultHref(item: EntityCard): string | undefined {
    if (
      item.kind === ENTITY_KIND.video &&
      item.parentKind === ENTITY_KIND.movie &&
      item.parentEntityId
    ) {
      return resolveEntityHref(ENTITY_KIND.movie, item.parentEntityId);
    }

    return resolveEntityHref(item.kind, item.id);
  }

  hrefFor(item: EntityCard): string | undefined {
    return this.#options.resolveHref?.(item) ?? this.#defaultHref(item);
  }

  setPageSize(pageSize: number) {
    const next = Math.max(1, Math.floor(pageSize));
    if (next === this.pageSize) return;
    this.pageSize = next;
    // During mount the grid announces its persisted page size before its initial
    // request-change event. Fetching here too would issue the page's most expensive
    // query twice back to back; the imminent initial load picks the new size up.
    if (!this.#loadStarted) return;
    void this.loadInitial();
  }
}
