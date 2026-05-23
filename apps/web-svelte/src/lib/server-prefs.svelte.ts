/**
 * Reactive DB-backed per-page UI preference helper. Use for values
 * that should follow the user across devices and sessions (thumbnail
 * sliders, saved filter presets, panel open/closed defaults). The
 * value is typed by the caller; the server treats it as opaque JSON.
 *
 * The helper debounces writes (250 ms) so a dragging slider does not
 * produce one request per movement. On SSR / before the first load
 * resolves, the caller reads the provided `defaults` synchronously.
 */

import { fetchApi as fetchApi } from "$lib/api/orval-fetch";

interface UiPrefResponse<T> {
  key: string;
  value: T | null;
}

export interface ServerPrefs<T> {
  /** Current value (reactive — wrap callers in $derived if needed). */
  readonly current: T;
  /** True once the initial server fetch has completed (or failed). */
  readonly loaded: boolean;
  /** Replace the entire value. */
  set(next: T): void;
  /** Mutate and write — merges partial patch into the current record. */
  update(patch: Partial<T>): void;
  /** Fetch from the server (called automatically on mount). */
  load(): Promise<void>;
  /** Force an immediate write, flushing any pending debounced write. */
  flush(): Promise<void>;
}

const DEBOUNCE_MS = 250;

export function createServerPrefs<T extends object>(
  key: string,
  defaults: T,
  initial?: Partial<T> | null,
  validate?: (raw: unknown) => T | null,
): ServerPrefs<T> {
  let value = $state<T>({ ...defaults, ...(initial ?? {}) });
  let loaded = $state(false);
  let pendingTimer: ReturnType<typeof setTimeout> | null = null;
  let pendingWrite: Promise<void> | null = null;

  async function writeNow() {
    if (pendingTimer) {
      clearTimeout(pendingTimer);
      pendingTimer = null;
    }
    const snapshot = $state.snapshot(value) as T;
    try {
      await fetchApi(`/ui-prefs/${encodeURIComponent(key)}`, {
        method: "PUT",
        body: JSON.stringify({ value: snapshot }),
      });
    } catch {
      // Best-effort: losing a write silently is better than losing
      // user interactions. The next change will retry.
    }
  }

  function scheduleWrite() {
    if (pendingTimer) clearTimeout(pendingTimer);
    pendingTimer = setTimeout(() => {
      pendingWrite = writeNow();
    }, DEBOUNCE_MS);
  }

  async function load() {
    if (typeof window === "undefined") return;
    try {
      const row = await fetchApi<UiPrefResponse<T>>(
        `/ui-prefs/${encodeURIComponent(key)}`,
      );
      if (row.value && typeof row.value === "object") {
        value = validate?.(row.value) ?? { ...defaults, ...(row.value as T) };
      }
    } catch {
      // fall back to defaults
    } finally {
      loaded = true;
    }
  }

  function set(next: T) {
    value = { ...next };
    scheduleWrite();
  }

  function update(patch: Partial<T>) {
    value = { ...value, ...patch };
    scheduleWrite();
  }

  async function flush() {
    if (pendingTimer) {
      await writeNow();
    } else if (pendingWrite) {
      await pendingWrite;
    }
  }

  return {
    get current() {
      return value;
    },
    get loaded() {
      return loaded;
    },
    set,
    update,
    load,
    flush,
  };
}
