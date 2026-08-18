import { fetchEntityShelf, type EntityShelfResponse } from "$lib/api/entities";
import type { ListEntityShelfParams } from "$lib/api/generated/model";

/**
 * Stale-while-revalidate cache for dashboard shelves. Returning to the dashboard renders the
 * last known shelves instantly while each shelf refreshes in the background, instead of holding
 * the whole page on a spinner for every navigation back to `/`.
 */
const TTL_MS = 60_000;
const MAX_ENTRIES = 24;

interface ShelfCacheEntry {
  at: number;
  response: EntityShelfResponse;
}

const cache = new Map<string, ShelfCacheEntry>();

function keyFor(params: ListEntityShelfParams | undefined): string {
  return JSON.stringify(params ?? {});
}

/** A shelf read that can serve a recent snapshot immediately while the network refresh runs. */
export interface ShelfRead {
  /** Snapshot from the last minute, or null on a cold read. */
  stale: EntityShelfResponse | null;
  /** The in-flight refresh; resolves with current server data and repopulates the cache. */
  fresh: Promise<EntityShelfResponse>;
}

/** Fetches a shelf with stale-while-revalidate semantics. */
export function fetchEntityShelfCached(params?: ListEntityShelfParams): ShelfRead {
  const key = keyFor(params);
  const entry = cache.get(key);
  const stale = entry && Date.now() - entry.at <= TTL_MS ? entry.response : null;
  const fresh = fetchEntityShelf(params).then((response) => {
    if (cache.size >= MAX_ENTRIES && !cache.has(key)) {
      const oldest = cache.keys().next().value;
      if (oldest !== undefined) cache.delete(oldest);
    }
    cache.set(key, { at: Date.now(), response });
    return response;
  });
  return { stale, fresh };
}

/** Drops all cached shelves (sign-out, library mutations). */
export function clearEntityShelfCache(): void {
  cache.clear();
}
