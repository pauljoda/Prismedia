import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { EntitySearchCandidate, PluginProvider } from "$lib/api/identify-types";
import type { EntityThumbnail as EntityCard } from "$lib/api/generated/model";
import IdentifyReviewChoice from "./IdentifyReviewChoice.svelte";

vi.mock("vidstack/player", () => ({}));
vi.mock("vidstack/player/layouts", () => ({}));
vi.mock("vidstack/player/ui", () => ({}));
vi.mock("vidstack", () => ({
  isHLSProvider: () => false,
}));

const store = vi.hoisted(() => ({
  error: null as string | null,
  busyEntityId: null as string | null,
  isItemBusy(entityId: string) {
    return this.busyEntityId === entityId;
  },
  itemSearchStatus(_entityId: string) {
    return null as string | null;
  },
  providers: [] as PluginProvider[],
  providersForKind: vi.fn(),
  identifyWithCandidate: vi.fn(),
  nextQueueItem: vi.fn(),
  rejectQueueItem: vi.fn(),
  navigateToDashboard: vi.fn(),
  identifyEntity: vi.fn(),
  waitForIdentifyResult: vi.fn(),
  reviewResolvedQueueItem: vi.fn(),
}));

vi.mock("./identify-store.svelte", () => ({
  useIdentifyStore: () => store,
}));

vi.mock("$lib/nsfw/store.svelte", () => ({
  useNsfw: () => ({ mode: "show" }),
}));

describe("IdentifyReviewChoice", () => {
  beforeEach(() => {
    globalThis.ResizeObserver = class {
      observe() {}
      disconnect() {}
      unobserve() {}
    };
    store.error = null;
    store.busyEntityId = null;
    store.providers = [];
    store.providersForKind.mockReset();
    store.providersForKind.mockReturnValue([provider()]);
    store.identifyWithCandidate.mockReset();
    store.nextQueueItem.mockReset();
    store.nextQueueItem.mockReturnValue(null);
    store.rejectQueueItem.mockReset();
    store.navigateToDashboard.mockReset();
    store.identifyEntity.mockReset();
    store.identifyEntity.mockResolvedValue({ state: "queued" });
    store.waitForIdentifyResult.mockReset();
    store.waitForIdentifyResult.mockResolvedValue(null);
    store.reviewResolvedQueueItem.mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("selects a candidate from the combined thumbnail and description card", async () => {
    const candidate = searchCandidate();
    const { container } = render(IdentifyReviewChoice, {
      props: {
        entity: entity(),
        candidates: [candidate],
      },
    });

    const card = screen.getByRole("button", { name: "Use The Chair Company (2025)" });
    expect(card).toHaveTextContent("A family man investigates a far-reaching conspiracy.");
    const thumbnail = container.querySelector<HTMLElement>(".identify-candidate-card > div");
    expect(thumbnail).not.toBeNull();
    expect(thumbnail?.getAttribute("tabindex")).toBeNull();
    expect(container.querySelector(`img[src="${candidate.posterUrl}"]`)).toHaveAttribute("referrerpolicy", "no-referrer");

    await fireEvent.click(thumbnail!);
    expect(store.identifyWithCandidate).toHaveBeenCalledWith(entity(), "tmdb", candidate);

    store.identifyWithCandidate.mockClear();
    await fireEvent.click(screen.getByText("A family man investigates a far-reaching conspiracy."));

    expect(store.identifyWithCandidate).toHaveBeenCalledWith(entity(), "tmdb", candidate);
  });

  it("opens candidate artwork from the eye preview without selecting the match", async () => {
    const candidate = searchCandidate();
    render(IdentifyReviewChoice, {
      props: {
        entity: entity(),
        candidates: [candidate],
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Preview The Chair Company artwork" }));

    expect(store.identifyWithCandidate).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "The Chair Company" })).toHaveAttribute("src", candidate.posterUrl);
    expect(screen.queryByRole("button", { name: "Rate 1" })).not.toBeInTheDocument();
  });

  it("rejects an ambiguous candidate search or rejects and advances to the next queue item", async () => {
    store.nextQueueItem.mockReturnValue({ entityId: "series-2" });

    render(IdentifyReviewChoice, {
      props: {
        entity: entity(),
        candidates: [searchCandidate()],
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Reject" }));
    expect(store.rejectQueueItem).toHaveBeenCalledWith("series-1");

    await fireEvent.click(screen.getByRole("button", { name: "Reject and Next" }));
    expect(store.rejectQueueItem).toHaveBeenCalledWith("series-1", { navigateNext: true });
  });

  it("shows loading only on the selected candidate while a tree is being checked", async () => {
    let resolveIdentify: () => void = () => undefined;
    store.identifyWithCandidate.mockReturnValue(new Promise<void>((resolve) => {
      resolveIdentify = resolve;
    }));

    const { container } = render(IdentifyReviewChoice, {
      props: {
        entity: entity(),
        candidates: [
          searchCandidate(),
          searchCandidate({ externalIds: { tmdb: "2" }, title: "Other Match" }),
        ],
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Use Other Match (2025)" }));

    await waitFor(() => {
      expect(screen.getByText("Match found. Identifying related items; this may take a while.")).toBeInTheDocument();
      expect(container.querySelectorAll(".animate-spin")).toHaveLength(1);
    });

    resolveIdentify();
    await waitFor(() => expect(container.querySelector(".animate-spin")).toBeNull());
  });

  it("lets the review query switch providers before searching again", async () => {
    store.providers = [
      provider("tmdb", "The Movie Database"),
      provider("anilist", "AniList"),
    ];
    store.providersForKind.mockReturnValue(store.providers);

    render(IdentifyReviewChoice, {
      props: {
        entity: entity(),
        candidates: [searchCandidate()],
        providerId: "tmdb",
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Provider: The Movie Database" }));
    await fireEvent.mouseDown(screen.getByRole("option", { name: "AniList anilist" }));

    expect(screen.getByRole("button", { name: "Provider: AniList" })).toBeInTheDocument();
  });

  it("uses stacked labeled fields for the review query form", () => {
    const { container } = render(IdentifyReviewChoice, {
      props: {
        entity: entity(),
        candidates: [searchCandidate()],
      },
    });

    const form = container.querySelector<HTMLElement>(".identify-query-form");
    const fields = container.querySelectorAll(".identify-query-field");

    expect(form).not.toBeNull();
    expect(form).toHaveClass("flex-col");
    expect(fields).toHaveLength(3);
    expect(fields[0]).toHaveTextContent("Provider");
    expect(fields[1]).toHaveTextContent("Query");
    expect(fields[2]).toHaveTextContent("Year");
    expect(screen.getByRole("button", { name: "Seek" })).toHaveClass("w-full");
    expect(screen.getByRole("button", { name: "Seek" })).toHaveClass("sm:w-24");
    expect(screen.getByPlaceholderText("Search titles...")).toHaveClass("w-full");
    expect(screen.getByPlaceholderText("Optional")).toHaveClass("w-full");
  });

  it("seeks through review query providers until candidates are found", async () => {
    store.providers = [
      provider("tmdb", "The Movie Database"),
      provider("anilist", "AniList"),
      provider("xvideos", "Xvideos"),
    ];
    store.providersForKind.mockReturnValue(store.providers);
    store.identifyEntity.mockResolvedValue({ state: "queued" });
    store.waitForIdentifyResult
      .mockResolvedValueOnce({ state: "error", provider: "anilist", candidates: [], proposal: null })
      .mockResolvedValueOnce({
        state: "search",
        provider: "xvideos",
        candidates: [searchCandidate({ externalIds: { xvideos: "1" }, title: "Provider Match" })],
        proposal: null,
      });

    render(IdentifyReviewChoice, {
      props: {
        entity: entity(),
        candidates: [searchCandidate()],
        providerId: "tmdb",
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Seek" }));

    await waitFor(() => expect(screen.getByText("Provider Match")).toBeInTheDocument());
    expect(store.identifyEntity).toHaveBeenNthCalledWith(1, entity(), "anilist", { title: "The Chair Company" });
    expect(store.identifyEntity).toHaveBeenNthCalledWith(2, entity(), "xvideos", { title: "The Chair Company" });
    expect(store.reviewResolvedQueueItem).not.toHaveBeenCalled();
  });

  it("shows the target preview and comic type label for book-kind comics", () => {
    render(IdentifyReviewChoice, {
      props: {
        entity: entity({
          kind: "book",
          title: "Always Go With the Flow!",
          meta: [{ icon: "book", label: "Comic" }],
        }),
        candidates: [],
      },
    });

    expect(screen.getByRole("button", { name: /To Identify/ })).toBeInTheDocument();
    expect(screen.getByText("Comic")).toBeInTheDocument();
  });
});

function provider(id = "tmdb", name = "The Movie Database"): PluginProvider {
  return {
    id,
    name,
    version: "1.0.0",
    installed: true,
    enabled: true,
    isNsfw: false,
    supports: [{ entityKind: "video-series", actions: ["search"] }],
    auth: [],
    missingAuthKeys: [],
  };
}

function entity(overrides: Partial<EntityCard> = {}): EntityCard {
  return {
    id: "series-1",
    kind: "video-series",
    title: "The Chair Company",
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
    ...overrides,
  };
}

function searchCandidate(overrides: Partial<EntitySearchCandidate> = {}): EntitySearchCandidate {
  return {
    externalIds: { tmdb: "271267" },
    overview: "A family man investigates a far-reaching conspiracy.",
    popularity: 42.84,
    posterUrl: "https://image.tmdb.org/t/p/w500/poster.jpg",
    title: "The Chair Company",
    year: 2025,
    ...overrides,
  };
}
