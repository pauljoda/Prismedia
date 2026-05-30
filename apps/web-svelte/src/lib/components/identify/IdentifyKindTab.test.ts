import { render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import IdentifyKindTab from "./IdentifyKindTab.svelte";
import type { EntityThumbnail as EntityCard } from "$lib/api/generated/model";

const fetchIdentifyEntities = vi.fn();
const goto = vi.fn();

const store = vi.hoisted(() => ({
  providersForKind: vi.fn(),
  supportedKinds: [
    { kind: "studio", label: "Studios", total: 0, unidentified: 0, pending: 0, hasProvider: true },
    { kind: "video-season", label: "Seasons", total: 0, unidentified: 0, pending: 0, hasProvider: true },
  ],
  error: null as string | null,
  bulkStarting: false,
  queueEntity: vi.fn(),
  startBulk: vi.fn(),
}));

vi.mock("$app/navigation", () => ({ goto: (...args: unknown[]) => goto(...args) }));

vi.mock("$lib/api/identify-client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("$lib/api/identify-client")>();
  return {
    ...actual,
    fetchIdentifyEntities: (...args: unknown[]) => fetchIdentifyEntities(...args),
  };
});

vi.mock("$lib/nsfw/store.svelte", () => ({
  useNsfw: () => ({ mode: "show" }),
}));

vi.mock("./identify-store.svelte", () => ({
  useIdentifyStore: () => store,
}));

describe("IdentifyKindTab", () => {
  beforeEach(() => {
    fetchIdentifyEntities.mockReset();
    goto.mockReset();
    store.providersForKind.mockReset();
    store.queueEntity.mockReset();
    store.startBulk.mockReset();
    store.error = null;
    store.providersForKind.mockReturnValue([provider()]);
    store.queueEntity.mockResolvedValue(null);
    Object.defineProperty(window, "localStorage", {
      configurable: true,
      value: {
        getItem: vi.fn(() => null),
        setItem: vi.fn(),
        removeItem: vi.fn(),
      },
    });
  });

  it("reloads the entity grid when the kind tab changes", async () => {
    fetchIdentifyEntities.mockImplementation((kind: string) =>
      Promise.resolve({
        items: kind === "studio"
          ? [entity("studio-1", { kind: "studio", title: "A24" })]
          : [entity("season-1", { kind: "video-season", title: "Season 1" })],
      }),
    );

    const { rerender } = render(IdentifyKindTab, { entityKind: "studio" });

    expect(await screen.findByText("A24")).toBeInTheDocument();

    await rerender({ entityKind: "video-season" });

    await waitFor(() => expect(fetchIdentifyEntities).toHaveBeenLastCalledWith("video-season"));
    expect(await screen.findByText("Season 1")).toBeInTheDocument();
    expect(screen.queryByText("A24")).not.toBeInTheDocument();
  });
});

function provider() {
  return {
    id: "tmdb",
    name: "The Movie Database",
    version: "1.0.0",
    installed: true,
    enabled: true,
    isNsfw: false,
    supports: [{ entityKind: "studio", actions: ["search"] }],
    auth: [],
    missingAuthKeys: [],
  };
}

function entity(id: string, options: Partial<EntityCard> = {}): EntityCard {
  return {
    id,
    kind: options.kind ?? "video",
    title: options.title ?? "Entity",
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
    ...options,
  };
}
