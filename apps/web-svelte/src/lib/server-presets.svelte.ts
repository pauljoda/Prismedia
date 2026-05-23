/**
 * Server-backed equivalent of `createFilterPresets` (which is
 * localStorage-backed). Each page maintains a list of named filter
 * presets as an opaque array stored under a single ui_prefs key.
 */

import type { FilterPreset } from "./filter-presets";
import { createServerPrefs } from "./server-prefs.svelte";

export type { FilterPreset };

export interface ServerPresetsApi {
  /** The current list of presets. Reactive. */
  readonly presets: FilterPreset[];
  /** True once the initial fetch has completed. */
  readonly loaded: boolean;
  load(): Promise<void>;
  save(next: FilterPreset[]): void;
  flush(): Promise<void>;
}

interface PresetBag {
  presets: FilterPreset[];
}

export function createServerPresets(key: string): ServerPresetsApi {
  const store = createServerPrefs<PresetBag>(key, { presets: [] });

  return {
    get presets() {
      return store.current.presets;
    },
    get loaded() {
      return store.loaded;
    },
    load: () => store.load(),
    save(next: FilterPreset[]) {
      store.set({ presets: next });
    },
    flush: () => store.flush(),
  };
}
