/**
 * View preferences for the `/series` root browser. Series filters share
 * the same filter-chip shape as videos, but translate into the
 * `/video-series` API query instead of video-card params. Prefs and
 * presets are persisted to the `ui_prefs` DB table so they follow you
 * across devices.
 */

import { isRecord, type SortDir, type DisplayFilterLookups } from "$lib/list-prefs";

export const SERIES_LIST_PREFS_KEY = "series:listPrefs";
export const SERIES_PRESETS_KEY = "series:filterPresets";

export type SeriesSortOption =
  | "recent"
  | "title"
  | "date"
  | "rating"
  | "videos"
  | "randomized";

export interface SeriesListPrefsActiveFilter {
  label: string;
  type: string;
  value: string;
}

export interface SeriesListPrefs {
  viewMode: "grid" | "list";
  sortBy: SeriesSortOption;
  sortDir: SortDir;
  search: string;
  activeFilters: SeriesListPrefsActiveFilter[];
  activePresetId?: string;
}

export interface SeriesFetchParams {
  root?: string;
  parent?: string;
  search?: string;
  sort?: string;
  order?: SortDir;
  tag?: string[];
  performer?: string[];
  studio?: string[];
  ratingMin?: number;
  ratingMax?: number;
  dateFrom?: string;
  dateTo?: string;
  organized?: string;
  limit?: number;
  offset?: number;
  nsfw?: string;
}

const SORT_OPTIONS: readonly SeriesSortOption[] = [
  "recent",
  "title",
  "date",
  "rating",
  "videos",
  "randomized",
];

function parseActiveFilters(raw: unknown): SeriesListPrefsActiveFilter[] | null {
  if (!Array.isArray(raw)) return null;
  const out: SeriesListPrefsActiveFilter[] = [];
  for (const item of raw) {
    if (!isRecord(item)) return null;
    const { label, type, value } = item;
    if (
      typeof label !== "string" ||
      typeof type !== "string" ||
      typeof value !== "string"
    ) {
      return null;
    }
    if (label.length > 200 || type.length > 64 || value.length > 200) return null;
    out.push({ label, type, value });
  }
  if (out.length > 80) return null;
  return out;
}

export function defaultSeriesListPrefs(): SeriesListPrefs {
  return {
    viewMode: "grid",
    sortBy: "recent",
    sortDir: "desc",
    search: "",
    activeFilters: [],
  };
}

export function isDefaultSeriesListPrefs(prefs: SeriesListPrefs): boolean {
  return JSON.stringify(prefs) === JSON.stringify(defaultSeriesListPrefs());
}

export function validateSeriesListPrefs(raw: unknown): SeriesListPrefs | null {
  if (!isRecord(raw)) return null;
  const sortBy = raw.sortBy;
  const sortDir = raw.sortDir;
  const viewMode = raw.viewMode;
  const search = raw.search;
  const activeFilters = parseActiveFilters(raw.activeFilters);

  if (typeof sortBy !== "string" || !SORT_OPTIONS.includes(sortBy as SeriesSortOption)) {
    return null;
  }
  const parsedViewMode = viewMode === "list" ? "list" : "grid";
  if (sortDir !== "asc" && sortDir !== "desc") return null;
  if (typeof search !== "string" || search.length > 500) return null;
  if (activeFilters === null) return null;

  const activePresetId =
    typeof raw.activePresetId === "string" ? raw.activePresetId : undefined;

  return {
    viewMode: parsedViewMode,
    sortBy: sortBy as SeriesSortOption,
    sortDir,
    search,
    activeFilters,
    activePresetId,
  };
}

export function seriesListPrefsToFetchParams(
  p: SeriesListPrefs,
  nsfw: string,
): SeriesFetchParams {
  const tagFilters = p.activeFilters.filter((f) => f.type === "tag").map((f) => f.value);
  const performerFilters = p.activeFilters
    .filter((f) => f.type === "performer")
    .map((f) => f.value);
  const studioFilters = p.activeFilters
    .filter((f) => f.type === "studio")
    .map((f) => f.value);
  const ratingMin = p.activeFilters.find((f) => f.type === "ratingMin")?.value;
  const ratingMax = p.activeFilters.find((f) => f.type === "ratingMax")?.value;
  const dateFrom = p.activeFilters.find((f) => f.type === "dateFrom")?.value;
  const dateTo = p.activeFilters.find((f) => f.type === "dateTo")?.value;
  const organized = p.activeFilters.find((f) => f.type === "organized")?.value;

  const rm = ratingMin !== undefined ? Number(ratingMin) : NaN;
  const rmax = ratingMax !== undefined ? Number(ratingMax) : NaN;

  return {
    search: p.search.trim() || undefined,
    sort: p.sortBy,
    order: p.sortDir,
    tag: tagFilters.length > 0 ? tagFilters : undefined,
    performer: performerFilters.length > 0 ? performerFilters : undefined,
    studio: studioFilters.length > 0 ? studioFilters : undefined,
    ratingMin: Number.isInteger(rm) && rm >= 1 && rm <= 5 ? rm : undefined,
    ratingMax: Number.isInteger(rmax) && rmax >= 1 && rmax <= 5 ? rmax : undefined,
    dateFrom: dateFrom && /^\d{4}-\d{2}-\d{2}$/.test(dateFrom) ? dateFrom : undefined,
    dateTo: dateTo && /^\d{4}-\d{2}-\d{2}$/.test(dateTo) ? dateTo : undefined,
    organized: organized === "true" || organized === "false" ? organized : undefined,
    nsfw,
  };
}

export function formatSeriesFilterValue(
  f: SeriesListPrefsActiveFilter,
  lookups: DisplayFilterLookups = {},
): string {
  switch (f.type) {
    case "studio":
      return lookups.studios?.find((s) => s.id === f.value)?.name ?? f.value;
    case "organized":
      return f.value === "true" ? "Yes" : "No";
    case "ratingMin":
      return `${f.value}★+`;
    case "ratingMax":
      return `≤${f.value}★`;
    default:
      return f.value;
  }
}

export const SERIES_EXCLUSIVE_FILTER_TYPES: ReadonlySet<string> = new Set([
  "ratingMin",
  "ratingMax",
  "dateFrom",
  "dateTo",
  "organized",
]);
