import { describe, expect, it, vi } from "vitest";
import type { EntityCard } from "$lib/api/generated/model";
import {
  entityFlagValue,
  toggleOptimisticEntityFlag,
  updateOptimisticEntityRating,
} from "./entity-detail-state";

function entity(): EntityCard {
  return {
    id: "video-1",
    kind: "video",
    title: "Video",
    parentEntityId: null,
    sortOrder: null,
    capabilities: [
      { kind: "rating", value: 2 },
      { kind: "flags", isFavorite: false, isNsfw: false, isOrganized: true },
    ],
    childrenByKind: [],
    relationships: [],
  };
}

describe("entity detail state helpers", () => {
  it("optimistically updates ratings and keeps the persisted value", async () => {
    let current = entity();
    const persist = vi.fn().mockResolvedValue(undefined);

    await updateOptimisticEntityRating(current, 4, (next) => (current = next), persist);

    expect(persist).toHaveBeenCalledWith("video-1", 4);
    expect(current.capabilities).toContainEqual({ kind: "rating", value: 4 });
  });

  it("rolls rating changes back when persistence fails", async () => {
    let current = entity();
    const previous = current;
    const persist = vi.fn().mockRejectedValue(new Error("offline"));

    await updateOptimisticEntityRating(current, 5, (next) => (current = next), persist);

    expect(current).toBe(previous);
  });

  it("toggles flags against the capability value and rolls back on failure", async () => {
    let current = entity();
    const persist = vi.fn().mockRejectedValue(new Error("offline"));

    expect(entityFlagValue(current, "isFavorite")).toBe(false);

    await toggleOptimisticEntityFlag(current, "isFavorite", (next) => (current = next), persist);

    expect(persist).toHaveBeenCalledWith("video-1", { isFavorite: true });
    expect(entityFlagValue(current, "isFavorite")).toBe(false);
  });
});
