import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { EntitySearchCandidate, PluginProvider } from "$lib/api/identify-types";
import type { EntityThumbnail as EntityCard } from "$lib/api/generated/model";
import IdentifyReviewChoice from "./IdentifyReviewChoice.svelte";

const store = vi.hoisted(() => ({
  error: null as string | null,
  identifyingId: null as string | null,
  providers: [] as PluginProvider[],
  providersForKind: vi.fn(),
  identifyWithCandidate: vi.fn(),
  navigateToDashboard: vi.fn(),
}));

vi.mock("./identify-store.svelte", () => ({
  useIdentifyStore: () => store,
}));

describe("IdentifyReviewChoice", () => {
  beforeEach(() => {
    store.error = null;
    store.identifyingId = null;
    store.providers = [];
    store.providersForKind.mockReset();
    store.providersForKind.mockReturnValue([provider()]);
    store.identifyWithCandidate.mockReset();
    store.navigateToDashboard.mockReset();
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
    const thumbnail = container.querySelector<HTMLElement>(".identify-candidate-card .entity-thumbnail");
    expect(thumbnail).not.toBeNull();
    expect(thumbnail?.getAttribute("tabindex")).toBeNull();

    await fireEvent.click(thumbnail!);
    expect(store.identifyWithCandidate).toHaveBeenCalledWith(entity(), "tmdb", candidate);

    store.identifyWithCandidate.mockClear();
    await fireEvent.click(screen.getByText("A family man investigates a far-reaching conspiracy."));

    expect(store.identifyWithCandidate).toHaveBeenCalledWith(entity(), "tmdb", candidate);
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

function entity(): EntityCard {
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
