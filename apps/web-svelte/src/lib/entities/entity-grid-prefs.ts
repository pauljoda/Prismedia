/**
 * Per-grid view-state persistence for {@link EntityGrid}.
 *
 * Every EntityGrid instance is identified by a stable `prefsKey` (typically the
 * route plus the entity kind it browses, e.g. `series` or `series-<id>-videos`).
 * This module turns that key into a dedicated cookie so the grid's filters,
 * sort, card size, media-wall toggle, page size, and active preset survive
 * reloads and naturally stay scoped to the device — no cross-device sync layer.
 *
 * The state is stored in a cookie (matching the `list-prefs` convention used
 * elsewhere) so dropping an EntityGrid on any page automatically restores the
 * last view the user left it in. Only non-default state is written; resetting a
 * grid back to its defaults clears the cookie so we never accumulate noise.
 */

import { createListPrefs, isRecord, type ListPrefsApi } from "$lib/list-prefs";
import type {
  EntityGridSort,
  EntityGridSortDir,
  EntityGridViewMode,
} from "./entity-grid";
import { ENTITY_GRID_ALL_KINDS } from "./entity-grid";

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
}

/**
 * Grid-specific fallbacks used when no cookie exists yet or a stored field is
 * missing/invalid. These mirror the EntityGrid props so each surface keeps its
 * own sensible starting view (e.g. index pages default to "added"/"desc").
 */
export interface EntityGridPrefsDefaults {
  sortBy: EntityGridSort;
  sortDir: EntityGridSortDir;
  mediaWall: boolean;
  scale: number;
  pageSize: number;
}

const COOKIE_PREFIX = "pm_eg_";
const VALID_SORTS: readonly EntityGridSort[] = ["title", "kind", "rating", "position", "added", "random"];
const VALID_SORT_DIRS: readonly EntityGridSortDir[] = ["asc", "desc"];
const VALID_VIEW_MODES: readonly EntityGridViewMode[] = ["grid", "list"];

/** Cookie tokens cannot contain separators/whitespace, so fold the key to a safe charset. */
function cookieNameFor(prefsKey: string): string {
  return `${COOKIE_PREFIX}${prefsKey.replace(/[^A-Za-z0-9_-]/g, "_")}`;
}

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
 * Builds the cookie-backed prefs store for a single grid.
 *
 * `validate` is lenient on purpose: any field that is missing or malformed
 * (including cookies written by an older grid version) falls back to the
 * supplied default, so persisted state always hydrates into a complete object
 * instead of being discarded wholesale.
 */
export function createEntityGridPrefs(
  prefsKey: string,
  defaults: EntityGridPrefsDefaults,
): ListPrefsApi<EntityGridPrefs> {
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
  });

  return createListPrefs<EntityGridPrefs>({
    cookieName: cookieNameFor(prefsKey),
    defaults: base,
    validate: (parsed: Record<string, unknown>): EntityGridPrefs => ({
      query: typeof parsed.query === "string" ? parsed.query : "",
      activeKind: typeof parsed.activeKind === "string" ? parsed.activeKind : ENTITY_GRID_ALL_KINDS,
      filterIds: stringArray(parsed.filterIds),
      includeNsfw: typeof parsed.includeNsfw === "boolean" ? parsed.includeNsfw : true,
      sortBy: oneOf(parsed.sortBy, VALID_SORTS, defaults.sortBy),
      sortDir: oneOf(parsed.sortDir, VALID_SORT_DIRS, defaults.sortDir),
      viewMode: oneOf(parsed.viewMode, VALID_VIEW_MODES, "grid"),
      mediaWall: typeof parsed.mediaWall === "boolean" ? parsed.mediaWall : defaults.mediaWall,
      scale: finiteNumber(parsed.scale, defaults.scale),
      pageSize: positiveInt(parsed.pageSize, defaults.pageSize),
      activePresetId: typeof parsed.activePresetId === "string" ? parsed.activePresetId : null,
    }),
  });
}

/** Re-exported for tests and callers that need the record guard without a second import. */
export { isRecord };
