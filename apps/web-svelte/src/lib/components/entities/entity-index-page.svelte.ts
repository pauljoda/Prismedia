import { fetchEntities, type EntityCard } from "$lib/api/entities";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import { entityCardToThumbnailCard, type EntityGridServerQuery } from "$lib/entities/entity-grid";
import { resolveEntityHref } from "$lib/entities/entity-routes";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

const DEFAULT_ENTITY_PAGE_SIZE = 250;

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
   * lock `bookType`). Spread after the user's {@link serverQuery} so the lock
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

  constructor(options: EntityIndexPageStateOptions) {
    this.#options = options;
  }

  async loadInitial() {
    this.#searchAbort?.abort();
    this.#searchAbort = new AbortController();
    const signal = this.#searchAbort.signal;

    this.loadState = "loading";
    this.errorMessage = null;
    this.loadMoreError = null;
    this.items = [];
    this.nextCursor = null;
    this.totalCount = 0;

    try {
      const response = await fetchEntities({
        kind: this.#options.getKind(),
        query: this.query || undefined,
        hideNsfw: this.#options.getHideNsfw(),
        limit: this.pageSize,
        ...this.serverQuery,
        ...this.#options.lockedServerQuery,
      }, { signal });
      if (signal.aborted) return;
      this.items = response.items;
      this.nextCursor = response.nextCursor;
      this.totalCount = requireTotalCount(response.totalCount);
      this.loadState = "ready";
    } catch (err) {
      if (signal.aborted || (err instanceof DOMException && err.name === "AbortError")) return;
      this.errorMessage = err instanceof Error ? err.message : String(err);
      this.loadState = "error";
    }
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
    void this.loadInitial();
  }
}
