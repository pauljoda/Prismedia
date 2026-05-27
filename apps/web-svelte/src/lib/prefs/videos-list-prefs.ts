/**
 * View preferences for the `/videos` route — sort, filters, search, and
 * view mode. Persisted to the `ui_prefs` DB table under the
 * `videos:listPrefs` key via `createServerPrefs`, so the SvelteKit load
 * function reads prefs via a single DB point-lookup (no cookie round
 * trip) and the client writes them through `PUT /api/ui-prefs/...`
 * before invalidating the load cache.
 *
 * Presets (save/apply named combinations of sort + filters) share the
 * same ui_prefs backing under the `videos:filterPresets` key via
 * `createServerPresets`, so they follow you across devices and browsers.
 */

import { isRecord, type SortDir, type DisplayFilterLookups } from "$lib/list-prefs";

export const VIDEOS_LIST_PREFS_KEY = "videos:listPrefs";
export const VIDEOS_PRESETS_KEY = "videos:filterPresets";

export type ViewMode = "grid" | "list" | "series";
export type SortOption =
  | "recent"
  | "title"
  | "duration"
  | "size"
  | "rating"
  | "date"
  | "plays"
  | "episode"
  | "randomized";

export interface VideosListPrefsActiveFilter {
  label: string;
  type: string;
  value: string;
}

export interface VideosListPrefs {
  viewMode: ViewMode;
  sortBy: SortOption;
  sortDir: SortDir;
  search: string;
  activeFilters: VideosListPrefsActiveFilter[];
  activePresetId?: string;
}

const VIEW_MODES: readonly ViewMode[] = ["grid", "list", "series"];
const SORT_OPTIONS: readonly SortOption[] = [
  "recent",
  "title",
  "duration",
  "size",
  "rating",
  "date",
  "plays",
  "episode",
  "randomized",
];

function parseActiveFilters(raw: unknown): VideosListPrefsActiveFilter[] | null {
  if (!Array.isArray(raw)) return null;
  const out: VideosListPrefsActiveFilter[] = [];
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

export function defaultVideosListPrefs(): VideosListPrefs {
  return {
    viewMode: "grid",
    sortBy: "recent",
    sortDir: "desc",
    search: "",
    activeFilters: [],
  };
}

export function isDefaultVideosListPrefs(prefs: VideosListPrefs): boolean {
  return JSON.stringify(prefs) === JSON.stringify(defaultVideosListPrefs());
}

/**
 * Validate an opaque JSON blob (from `ui_prefs.value`) as a
 * VideosListPrefs. Returns null if any field fails validation, which
 * the caller should treat as "fall back to defaults".
 */
export function validateVideosListPrefs(raw: unknown): VideosListPrefs | null {
  if (!isRecord(raw)) return null;
  const viewMode = raw.viewMode;
  const sortBy = raw.sortBy;
  const sortDir = raw.sortDir;
  const search = raw.search;
  const activeFilters = parseActiveFilters(raw.activeFilters);

  if (typeof viewMode !== "string" || !VIEW_MODES.includes(viewMode as ViewMode)) {
    return null;
  }
  if (typeof sortBy !== "string" || !SORT_OPTIONS.includes(sortBy as SortOption)) {
    return null;
  }
  if (sortDir !== "asc" && sortDir !== "desc") return null;
  if (typeof search !== "string" || search.length > 500) return null;
  if (activeFilters === null) return null;

  const activePresetId =
    typeof raw.activePresetId === "string" ? raw.activePresetId : undefined;

  return {
    viewMode: viewMode as ViewMode,
    sortBy: sortBy as SortOption,
    sortDir,
    search,
    activeFilters,
    activePresetId,
  };
}

const DURATION_PRESET_TO_API: Record<
  string,
  { durationMin?: number; durationMax?: number }
> = {
  lt300: { durationMax: 300 },
  "300-900": { durationMin: 300, durationMax: 900 },
  "900-1800": { durationMin: 900, durationMax: 1800 },
  gte1800: { durationMin: 1800 },
};

export interface VideosFetchParams {
  search?: string;
  sort?: SortOption;
  order?: SortDir;
  tag?: string[];
  performer?: string[];
  resolution?: string[];
  studio?: string[];
  ratingMin?: number;
  ratingMax?: number;
  dateFrom?: string;
  dateTo?: string;
  durationMin?: number;
  durationMax?: number;
  organized?: string;
  isNsfw?: string;
  interactive?: string;
  hasFile?: string;
  played?: string;
  codec?: string[];
  nsfw?: string;
}

/**
 * Translate the cookie-backed list prefs into the API query shape
 * expected by `fetchVideoCards`. Duration is stored as a preset id
 * ("lt300" / "300-900" / …) but transmitted as numeric min/max seconds.
 */
export function videosListPrefsToFetchParams(
  p: VideosListPrefs,
  nsfw: string,
): VideosFetchParams {
  const tagFilters = p.activeFilters.filter((f) => f.type === "tag").map((f) => f.value);
  const performerFilters = p.activeFilters
    .filter((f) => f.type === "performer")
    .map((f) => f.value);
  const resolutionFilters = p.activeFilters
    .filter((f) => f.type === "resolution")
    .map((f) => f.value);
  const studioFilters = p.activeFilters
    .filter((f) => f.type === "studio")
    .map((f) => f.value);
  const codecFilters = p.activeFilters
    .filter((f) => f.type === "codec")
    .map((f) => f.value.toLowerCase());
  const ratingMin = p.activeFilters.find((f) => f.type === "ratingMin")?.value;
  const ratingMax = p.activeFilters.find((f) => f.type === "ratingMax")?.value;
  const dateFrom = p.activeFilters.find((f) => f.type === "dateFrom")?.value;
  const dateTo = p.activeFilters.find((f) => f.type === "dateTo")?.value;
  const durationPreset = p.activeFilters.find((f) => f.type === "duration")?.value;
  const organized = p.activeFilters.find((f) => f.type === "organized")?.value;
  const isNsfw = p.activeFilters.find((f) => f.type === "isNsfw")?.value;
  const interactive = p.activeFilters.find((f) => f.type === "interactive")?.value;
  const hasFile = p.activeFilters.find((f) => f.type === "hasFile")?.value;
  const played = p.activeFilters.find((f) => f.type === "played")?.value;

  const dur =
    durationPreset && DURATION_PRESET_TO_API[durationPreset]
      ? DURATION_PRESET_TO_API[durationPreset]
      : {};

  const rm = ratingMin !== undefined ? Number(ratingMin) : NaN;
  const rmax = ratingMax !== undefined ? Number(ratingMax) : NaN;

  return {
    search: p.search.trim() || undefined,
    sort: p.sortBy,
    order: p.sortDir,
    tag: tagFilters.length > 0 ? tagFilters : undefined,
    performer: performerFilters.length > 0 ? performerFilters : undefined,
    resolution: resolutionFilters.length > 0 ? resolutionFilters : undefined,
    studio: studioFilters.length > 0 ? studioFilters : undefined,
    ratingMin: Number.isInteger(rm) && rm >= 1 && rm <= 5 ? rm : undefined,
    ratingMax: Number.isInteger(rmax) && rmax >= 1 && rmax <= 5 ? rmax : undefined,
    dateFrom: dateFrom && /^\d{4}-\d{2}-\d{2}$/.test(dateFrom) ? dateFrom : undefined,
    dateTo: dateTo && /^\d{4}-\d{2}-\d{2}$/.test(dateTo) ? dateTo : undefined,
    durationMin: dur.durationMin,
    durationMax: dur.durationMax,
    organized:
      organized === "true" || organized === "false" ? organized : undefined,
    isNsfw:
      isNsfw === "true" || isNsfw === "false" ? isNsfw : undefined,
    interactive:
      interactive === "true" || interactive === "false" ? interactive : undefined,
    hasFile: hasFile === "true" || hasFile === "false" ? hasFile : undefined,
    played: played === "true" || played === "false" ? played : undefined,
    codec: codecFilters.length > 0 ? codecFilters : undefined,
    nsfw,
  };
}

const DURATION_LABELS: Record<string, string> = {
  lt300: "< 5 min",
  "300-900": "5–15 min",
  "900-1800": "15–30 min",
  gte1800: "30+ min",
};
const CODEC_LABELS: Record<string, string> = {
  h264: "H.264",
  hevc: "HEVC",
  av1: "AV1",
  vp9: "VP9",
  vp8: "VP8",
  mpeg4: "MPEG-4",
  prores: "ProRes",
  wmv: "WMV",
};

export function formatFilterValue(
  f: VideosListPrefsActiveFilter,
  lookups: DisplayFilterLookups = {},
): string {
  switch (f.type) {
    case "studio":
      return lookups.studios?.find((s) => s.id === f.value)?.name ?? f.value;
    case "played":
      return f.value === "true" ? "Played" : "Unplayed";
    case "hasFile":
      return f.value === "true" ? "Has file" : "No file";
    case "organized":
    case "interactive":
      return f.value === "true" ? "Yes" : "No";
    case "duration":
      return DURATION_LABELS[f.value] ?? f.value;
    case "codec":
      return CODEC_LABELS[f.value] ?? f.value;
    case "ratingMin":
      return `${f.value}★+`;
    case "ratingMax":
      return `≤${f.value}★`;
    default:
      return f.value;
  }
}

/**
 * Filter types that allow only one value at a time; selecting a second
 * value of the same type replaces the first. Multi-select types (tag,
 * performer, studio, codec, resolution) toggle values in/out of a list.
 */
export const EXCLUSIVE_FILTER_TYPES: ReadonlySet<string> = new Set([
  "ratingMin",
  "ratingMax",
  "dateFrom",
  "dateTo",
  "duration",
  "organized",
  "interactive",
  "hasFile",
  "played",
]);
