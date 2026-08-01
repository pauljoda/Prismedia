import type { FilterPreset } from "$lib/filter-presets";
import {
  entityGridFilterFromId,
  normalizeEntityGridFilterIds,
  type EntityGridFilterOption,
  type EntityGridSort,
  type EntityGridSortDir,
} from "$lib/entities/entity-grid";

const ENTITY_GRID_PRESET_SORTS: EntityGridSort[] = [
  "title",
  "kind",
  "rating",
  "position",
  "added",
  "random",
];

interface CreateEntityGridPresetInput {
  id: string;
  name: string;
  filterIds: string[];
  filterOptions: EntityGridFilterOption[];
  sortBy: EntityGridSort;
  sortDir: EntityGridSortDir;
}

interface ReadEntityGridPresetInput {
  preset: FilterPreset;
  filterOptions: EntityGridFilterOption[];
  fallbackSortBy: EntityGridSort;
}

/** Returns the localStorage key for an EntityGrid's saved filter presets. */
export function entityGridPresetStorageKey(prefsKey: string | undefined): string | null {
  return prefsKey ? `prismedia:entity-grid-presets:${prefsKey}` : null;
}

/** Creates a new EntityGrid preset identifier using the existing timestamp format. */
export function createEntityGridPresetId(now: number = Date.now()): string {
  return `entity-grid-preset-${now.toString(36)}`;
}

/**
 * Shapes the grid's active controls into the persisted filter-preset contract.
 * Filter labels are a presentation snapshot; their ids remain the apply-time source of truth.
 */
export function createEntityGridPreset({
  id,
  name,
  filterIds,
  filterOptions,
  sortBy,
  sortDir,
}: CreateEntityGridPresetInput): FilterPreset {
  return {
    id,
    name,
    filters: filterIds.map((filterId) => {
      const option = entityGridFilterFromId(filterId, filterOptions);
      return {
        label: option?.label ?? filterId,
        type: option?.capabilityKind ?? "capability",
        value: filterId,
      };
    }),
    sortBy,
    sortDir,
  };
}

/**
 * Normalizes a stored preset against the filters available on the current
 * EntityGrid, dropping stale ids and returning a safe sort fallback.
 */
export function readEntityGridPreset({
  preset,
  filterOptions,
  fallbackSortBy,
}: ReadEntityGridPresetInput): Pick<FilterPreset, "sortDir"> & {
  filterIds: string[];
  sortBy: EntityGridSort;
} {
  return {
    filterIds: normalizeEntityGridFilterIds(preset.filters.map((filter) => filter.value))
      .filter((filterId) => Boolean(entityGridFilterFromId(filterId, filterOptions))),
    sortBy: ENTITY_GRID_PRESET_SORTS.includes(preset.sortBy as EntityGridSort)
      ? preset.sortBy as EntityGridSort
      : fallbackSortBy,
    sortDir: preset.sortDir,
  };
}
