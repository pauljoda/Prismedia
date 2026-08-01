import { describe, expect, it } from "vitest";
import {
  createEntityGridPreset,
  createEntityGridPresetId,
  entityGridPresetStorageKey,
  readEntityGridPreset,
} from "./entity-grid-filter-presets";
import type { EntityGridFilterOption } from "$lib/entities/entity-grid";

const filterOptions: EntityGridFilterOption[] = [{
  id: "flag:isFavorite",
  label: "Favorite",
  count: 1,
  capabilityKind: "flags",
}];

describe("entity-grid filter presets", () => {
  it("creates a persisted preset from the active filters and current sort", () => {
    expect(createEntityGridPreset({
      id: "preset-1",
      name: "Favorites",
      filterIds: ["flag:isFavorite"],
      filterOptions,
      sortBy: "title",
      sortDir: "asc",
    })).toEqual({
      id: "preset-1",
      name: "Favorites",
      filters: [{ label: "Favorite", type: "flags", value: "flag:isFavorite" }],
      sortBy: "title",
      sortDir: "asc",
    });
  });

  it("drops stale filters and falls back from unsupported stored sorts", () => {
    expect(readEntityGridPreset({
      preset: {
        id: "preset-1",
        name: "Legacy",
        filters: [
          { label: "Favorite", type: "flags", value: "flag:isFavorite" },
          { label: "Removed", type: "flags", value: "flag:removed" },
        ],
        sortBy: "legacy-sort",
        sortDir: "desc",
      },
      filterOptions,
      fallbackSortBy: "added",
    })).toEqual({
      filterIds: ["flag:isFavorite"],
      sortBy: "added",
      sortDir: "desc",
    });
  });

  it("keeps preset storage and identifier formats stable", () => {
    expect(entityGridPresetStorageKey("videos")).toBe("prismedia:entity-grid-presets:videos");
    expect(entityGridPresetStorageKey(undefined)).toBeNull();
    expect(createEntityGridPresetId(36)).toBe("entity-grid-preset-10");
  });
});
