/**
 * Generic localStorage-backed filter preset collections.
 *
 * Each entity view (videos, galleries, …) that has a filter bar creates
 * its own preset store keyed on a unique storage key, then uses the
 * returned `load`/`save` helpers to drive `<FilterPresetDropdown>`.
 * Framework-agnostic — safe to import from SvelteKit server code even
 * though `load`/`save` no-op there.
 */

import { isRecord } from "./list-prefs";

export interface FilterPresetActiveFilter {
  label: string;
  type: string;
  value: string;
}

export interface FilterPreset {
  id: string;
  name: string;
  filters: FilterPresetActiveFilter[];
  sortBy: string;
  sortDir: "asc" | "desc";
}

export interface FilterPresetsApi {
  storageKey: string;
  load: () => FilterPreset[];
  save: (presets: FilterPreset[]) => void;
}

const DEFAULT_MAX_PRESETS = 20;

function validatePreset(raw: unknown): FilterPreset | null {
  if (!isRecord(raw)) return null;
  const { id, name, filters, sortBy, sortDir } = raw;
  if (typeof id !== "string" || typeof name !== "string") return null;
  if (typeof sortBy !== "string" || typeof sortDir !== "string") return null;
  if (sortDir !== "asc" && sortDir !== "desc") return null;
  if (!Array.isArray(filters)) return null;
  const out: FilterPresetActiveFilter[] = [];
  for (const f of filters) {
    if (!isRecord(f)) return null;
    if (
      typeof f.label !== "string" ||
      typeof f.type !== "string" ||
      typeof f.value !== "string"
    ) {
      return null;
    }
    out.push({ label: f.label, type: f.type, value: f.value });
  }
  return {
    id,
    name: name.slice(0, 100),
    filters: out,
    sortBy,
    sortDir,
  };
}

export function createFilterPresets(
  storageKey: string,
  maxPresets: number = DEFAULT_MAX_PRESETS,
): FilterPresetsApi {
  function load(): FilterPreset[] {
    if (typeof window === "undefined") return [];
    try {
      const raw = window.localStorage.getItem(storageKey);
      if (!raw) return [];
      const parsed: unknown = JSON.parse(raw);
      if (!Array.isArray(parsed)) return [];
      const presets: FilterPreset[] = [];
      for (const item of parsed) {
        const preset = validatePreset(item);
        if (preset) presets.push(preset);
        if (presets.length >= maxPresets) break;
      }
      return presets;
    } catch {
      return [];
    }
  }

  function save(presets: FilterPreset[]): void {
    if (typeof window === "undefined") return;
    try {
      window.localStorage.setItem(
        storageKey,
        JSON.stringify(presets.slice(0, maxPresets)),
      );
    } catch {
      // localStorage full or unavailable — preset save is best-effort.
    }
  }

  return { storageKey, load, save };
}
