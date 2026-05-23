import { browser } from "$app/environment";

const STORAGE_KEY = "prismedia:recent-searches";
const MAX_RECENT = 8;

function read(): string[] {
  if (!browser) return [];
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as string[]) : [];
  } catch {
    return [];
  }
}

/**
 * Rune-based store for recent-search localStorage entries. Subscribes to
 * the `storage` event so multiple tabs stay in sync.
 */
export function recentSearches() {
  let value = $state<string[]>(read());

  function persist(next: string[]) {
    value = next;
    if (!browser) return;
    if (next.length === 0) localStorage.removeItem(STORAGE_KEY);
    else localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  }

  if (browser) {
    $effect.root(() => {
      const handler = (e: StorageEvent) => {
        if (e.key !== STORAGE_KEY) return;
        try {
          value = e.newValue ? (JSON.parse(e.newValue) as string[]) : [];
        } catch {
          value = [];
        }
      };
      window.addEventListener("storage", handler);
      return () => window.removeEventListener("storage", handler);
    });
  }

  function add(query: string) {
    const trimmed = query.trim();
    if (!trimmed) return;
    const current = read();
    persist([trimmed, ...current.filter((s) => s !== trimmed)].slice(0, MAX_RECENT));
  }

  function remove(query: string) {
    persist(read().filter((s) => s !== query));
  }

  function clear() {
    persist([]);
  }

  return {
    get value() {
      return value;
    },
    add,
    remove,
    clear,
  };
}
