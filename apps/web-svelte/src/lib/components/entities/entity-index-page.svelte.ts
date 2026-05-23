import { fetchEntities, type EntityCard } from "$lib/api/prismedia";
import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
import { resolveEntityHref } from "$lib/entities/entity-routes";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

const DEFAULT_ENTITY_PAGE_SIZE = 250;

/**
 * Orval emits the OpenAPI int32 totalCount as `number | string` to honour the spec's
 * pattern constraint; the .NET API always serializes it as a number, but we coerce
 * defensively so a string from an older runtime or a manual response shim is still
 * treated as a count rather than NaN.
 */
function coerceTotalCount(value: number | string | undefined, fallback: number): number {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value === "string") {
    const parsed = Number(value);
    if (Number.isFinite(parsed)) return parsed;
  }
  return fallback;
}

export type EntityIndexLoadState = "loading" | "ready" | "error";

export interface EntityIndexPageStateOptions {
  getKind: () => string;
  getHideNsfw: () => boolean;
  resolveHref?: (item: EntityCard) => string | undefined;
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
      }, { signal });
      if (signal.aborted) return;
      this.items = response.items;
      this.nextCursor = response.nextCursor;
      this.totalCount = coerceTotalCount(response.totalCount, response.items.length);
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
      });
      this.items = [...this.items, ...response.items];
      this.nextCursor = response.nextCursor;
      this.totalCount = coerceTotalCount(response.totalCount, this.totalCount);
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

  #defaultHref(item: EntityCard): string | undefined {
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
