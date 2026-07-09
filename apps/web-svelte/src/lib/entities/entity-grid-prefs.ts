/**
 * Per-grid view-state persistence for {@link EntityGrid}.
 *
 * Every EntityGrid instance is identified by a stable `prefsKey` (typically the
 * route plus the entity kind it browses, e.g. `series` or `series-<id>-videos`).
 * This module turns that key into a dedicated `localStorage` entry so the grid's
 * filters, sort, card size, media-wall toggle, page size, and active preset
 * survive reloads and stay scoped to the device — no cross-device sync layer and
 * no per-request cookie overhead.
 *
 * The state is kept client-side in `localStorage` (the app runs as an SPA, so
 * there is nothing for the server to read), and only non-default state is
 * written: resetting a grid back to its defaults clears the entry so we never
 * accumulate noise. The store is deliberately small and validation is lenient,
 * so the persisted shape can grow new fields over time without invalidating
 * entries written by older builds.
 */

import { isRecord } from "$lib/list-prefs";
import type {
  EntityGridSort,
  EntityGridSortDir,
  EntityGridViewMode,
} from "./entity-grid";
import { ENTITY_GRID_ALL_KINDS, normalizeEntityGridFilterIds } from "./entity-grid";

/** The full set of EntityGrid controls that persist across reloads, per grid key. */
export interface EntityGridPrefs {
  query: string;
  activeKind: string;
  filterIds: string[];
  includeNsfw: boolean;
  sortBy: EntityGridSort;
  sortDir: EntityGridSortDir;
  viewMode: EntityGridViewMode;
  mediaWall: boolean;
  scale: number;
  pageSize: number;
  activePresetId: string | null;
  /** Whether the secondary toolbar rows (filters/selection) are collapsed. */
  barsCollapsed: boolean;
}

/**
 * Grid-specific fallbacks used when no stored state exists yet or a stored field
 * is missing/invalid. These mirror the EntityGrid props so each surface keeps its
 * own sensible starting view (e.g. index pages default to "added"/"desc").
 */
export interface EntityGridPrefsDefaults {
  sortBy: EntityGridSort;
  sortDir: EntityGridSortDir;
  mediaWall: boolean;
  scale: number;
  pageSize: number;
}

/**
 * A localStorage-backed view-state store scoped to a single grid. Kept as a
 * small explicit surface (rather than free functions) so additional persistence
 * concerns — migrations, schema versioning, alternate backends — can be layered
 * in later without touching the EntityGrid component.
 */
export interface EntityGridPrefsStore {
  /** The localStorage key this grid's view state is stored under. */
  storageKey: string;
  /** The default view state for this grid, used for comparison and resets. */
  defaults: () => EntityGridPrefs;
  /** True when `prefs` equals the defaults, i.e. there is nothing worth persisting. */
  isDefault: (prefs: EntityGridPrefs) => boolean;
  /** Reads and validates the stored state, or `null` when absent or unreadable. */
  load: () => EntityGridPrefs | null;
  /** Persists the full state. Best-effort: storage errors are swallowed. */
  save: (prefs: EntityGridPrefs) => void;
  /** Removes any stored state for this grid. */
  clear: () => void;
}

const STORAGE_PREFIX = "prismedia:entity-grid-state:";
const VALID_SORTS: readonly EntityGridSort[] = ["title", "kind", "rating", "position", "added", "random", "references"];
const VALID_SORT_DIRS: readonly EntityGridSortDir[] = ["asc", "desc"];
const VALID_VIEW_MODES: readonly EntityGridViewMode[] = ["grid", "list", "feed"];

function oneOf<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
  return typeof value === "string" && (allowed as readonly string[]).includes(value) ? (value as T) : fallback;
}

function stringArray(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  return value.filter((entry): entry is string => typeof entry === "string");
}

function positiveInt(value: unknown, fallback: number): number {
  const numeric = typeof value === "number" ? Math.floor(value) : NaN;
  return Number.isFinite(numeric) && numeric > 0 ? numeric : fallback;
}

function finiteNumber(value: unknown, fallback: number): number {
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

/**
 * Builds the localStorage-backed prefs store for a single grid.
 *
 * `validate` is lenient on purpose: any field that is missing or malformed
 * (including entries written by an older grid version) falls back to the
 * supplied default, so persisted state always hydrates into a complete object
 * instead of being discarded wholesale.
 */
export function createEntityGridPrefs(
  prefsKey: string,
  defaults: EntityGridPrefsDefaults,
): EntityGridPrefsStore {
  const storageKey = `${STORAGE_PREFIX}${prefsKey}`;

  const base = (): EntityGridPrefs => ({
    query: "",
    activeKind: ENTITY_GRID_ALL_KINDS,
    filterIds: [],
    includeNsfw: true,
    sortBy: defaults.sortBy,
    sortDir: defaults.sortDir,
    viewMode: "grid",
    mediaWall: defaults.mediaWall,
    scale: defaults.scale,
    pageSize: defaults.pageSize,
    activePresetId: null,
    barsCollapsed: false,
  });

  const validate = (parsed: Record<string, unknown>): EntityGridPrefs => ({
    query: typeof parsed.query === "string" ? parsed.query : "",
    activeKind: typeof parsed.activeKind === "string" ? parsed.activeKind : ENTITY_GRID_ALL_KINDS,
    filterIds: normalizeEntityGridFilterIds(stringArray(parsed.filterIds)),
    includeNsfw: typeof parsed.includeNsfw === "boolean" ? parsed.includeNsfw : true,
    sortBy: oneOf(parsed.sortBy, VALID_SORTS, defaults.sortBy),
    sortDir: oneOf(parsed.sortDir, VALID_SORT_DIRS, defaults.sortDir),
    viewMode: oneOf(parsed.viewMode, VALID_VIEW_MODES, "grid"),
    mediaWall: typeof parsed.mediaWall === "boolean" ? parsed.mediaWall : defaults.mediaWall,
    scale: finiteNumber(parsed.scale, defaults.scale),
    pageSize: positiveInt(parsed.pageSize, defaults.pageSize),
    activePresetId: typeof parsed.activePresetId === "string" ? parsed.activePresetId : null,
    barsCollapsed: typeof parsed.barsCollapsed === "boolean" ? parsed.barsCollapsed : false,
  });

  function load(): EntityGridPrefs | null {
    if (typeof window === "undefined") return null;
    try {
      const raw = window.localStorage.getItem(storageKey);
      if (!raw) return null;
      const parsed: unknown = JSON.parse(raw);
      if (!isRecord(parsed)) return null;
      return validate(parsed);
    } catch {
      return null;
    }
  }

  function save(prefs: EntityGridPrefs): void {
    if (typeof window === "undefined") return;
    try {
      window.localStorage.setItem(storageKey, JSON.stringify(prefs));
    } catch {
      // localStorage full or unavailable — view-state persistence is best-effort.
    }
  }

  function clear(): void {
    if (typeof window === "undefined") return;
    try {
      window.localStorage.removeItem(storageKey);
    } catch {
      // Ignore storage access errors.
    }
  }

  function isDefault(prefs: EntityGridPrefs): boolean {
    return JSON.stringify(prefs) === JSON.stringify(base());
  }

  return { storageKey, defaults: base, isDefault, load, save, clear };
}
