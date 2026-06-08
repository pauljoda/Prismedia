import { describe, expect, it } from "vitest";
import type { IdentifyQueueItem } from "./identify-store.svelte";
import { shouldShowRouteQueueRejectActions } from "./identify-route-actions";

describe("identify route actions", () => {
  it("hides route reject actions while the current item is actively identifying", () => {
    expect(
      shouldShowRouteQueueRejectActions({
        current: queueItem(),
        reviewSurfaceHasRejectFooter: false,
        isIdentifyingCurrent: true,
      }),
    ).toBe(false);
  });

  it("keeps route reject actions for idle queue states without a review footer", () => {
    expect(
      shouldShowRouteQueueRejectActions({
        current: queueItem(),
        reviewSurfaceHasRejectFooter: false,
        isIdentifyingCurrent: false,
      }),
    ).toBe(true);
  });

  it("defers to review surfaces that already own their reject footer", () => {
    expect(
      shouldShowRouteQueueRejectActions({
        current: queueItem(),
        reviewSurfaceHasRejectFooter: true,
        isIdentifyingCurrent: false,
      }),
    ).toBe(false);
  });
});

function queueItem(): IdentifyQueueItem {
  return {
    id: "queue-entity-1",
    entityId: "entity-1",
    entityKind: "video",
    title: "Queued Video",
    isNsfw: false,
    state: "search",
    provider: null,
    action: "search",
    candidates: [],
    proposal: null,
    errorMessage: null,
    cascadeRunning: false,
    entity: {
      id: "entity-1",
      kind: "video",
      title: "Queued Video",
      parentEntityId: null,
      sortOrder: null,
      coverUrl: null,
      coverThumbUrl: null,
      hoverKind: "none",
      hoverUrl: null,
      hoverImages: [],
      meta: [],
      rating: null,
      isFavorite: false,
      isNsfw: false,
      isOrganized: false,
    },
    detail: null,
    completedAt: null,
  };
}
