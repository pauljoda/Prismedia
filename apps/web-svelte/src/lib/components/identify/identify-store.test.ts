import { beforeEach, describe, expect, it, vi } from "vitest";
import { IdentifyStore } from "./identify-store.svelte";
import type { EntityMetadataProposal } from "$lib/api/identify";
import type { EntityCard } from "$lib/api/prismedia";

const fetchPluginProviders = vi.fn();
const fetchIdentifyQueue = vi.fn();

vi.mock("$lib/api/identify", async (importOriginal) => {
  const actual = await importOriginal<typeof import("$lib/api/identify")>();
  return {
    ...actual,
    fetchPluginProviders: (...args: unknown[]) => fetchPluginProviders(...args),
    fetchIdentifyQueue: (...args: unknown[]) => fetchIdentifyQueue(...args),
  };
});

describe("IdentifyStore", () => {
  beforeEach(() => {
    fetchPluginProviders.mockReset();
    fetchIdentifyQueue.mockReset();
    fetchPluginProviders.mockResolvedValue([]);
    fetchIdentifyQueue.mockResolvedValue([]);
  });

  it("resets stale review state when entering the dashboard route", async () => {
    const store = new IdentifyStore();
    store.view = {
      kind: "review-parent",
      entity: entity("series-1"),
      proposal: proposal("proposal-1"),
      detail: null,
    };

    await store.enterDashboardRoute();

    expect(store.view.kind).toBe("dashboard");
    expect(fetchPluginProviders).toHaveBeenCalledOnce();
    expect(fetchIdentifyQueue).toHaveBeenCalledOnce();
  });
});

function entity(id: string): EntityCard {
  return {
    id,
    kind: "video-series",
    title: "Series",
    parentEntityId: null,
    sortOrder: null,
    coverUrl: null,
    hoverKind: "none",
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: false,
  };
}

function proposal(proposalId: string): EntityMetadataProposal {
  return {
    proposalId,
    provider: "tmdb",
    targetKind: "video-series",
    confidence: 1,
    matchReason: "test",
    patch: {
      title: "Series",
      description: null,
      externalIds: {},
      urls: [],
      tags: [],
      studio: null,
      credits: [],
      dates: {},
      stats: {},
      positions: {},
      classification: null,
    },
    images: [],
    children: [],
    relationships: [],
    candidates: [],
    targetEntityId: null,
  };
}
