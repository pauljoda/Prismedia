import { render, screen } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import IdentifyDashboard from "./IdentifyDashboard.svelte";

const store = vi.hoisted(() => ({
  loading: false,
  providers: [] as Array<{
    id: string;
    name: string;
    version: string;
    installed: boolean;
    enabled: boolean;
    isNsfw: boolean;
    supports: Array<{ entityKind: string; actions: string[] }>;
    auth: unknown[];
    missingAuthKeys: string[];
  }>,
  queue: [] as unknown[],
  supportedKinds: [] as Array<{
    kind: string;
    label: string;
    total: number;
    unidentified: number;
    pending: number;
    hasProvider: boolean;
  }>,
  navigateToKind: vi.fn(),
  resumeNext: vi.fn(),
  reviewQueueItem: vi.fn(),
}));

vi.mock("./identify-store.svelte", () => ({
  useIdentifyStore: () => store,
}));

describe("IdentifyDashboard", () => {
  beforeEach(() => {
    store.loading = false;
    store.providers = [provider()];
    store.queue = [];
    store.supportedKinds = [
      {
        kind: "video",
        label: "Videos",
        total: 0,
        unidentified: 0,
        pending: 0,
        hasProvider: true,
      },
    ];
    store.navigateToKind.mockReset();
    store.resumeNext.mockReset();
    store.reviewQueueItem.mockReset();
  });

  it("renders dashboard content without summary stats or plugin inventory", () => {
    render(IdentifyDashboard);

    expect(screen.getByText("Browse by kind")).toBeInTheDocument();
    expect(screen.queryByText("Queue")).not.toBeInTheDocument();
    expect(screen.queryByText("Pending")).not.toBeInTheDocument();
    expect(screen.queryByText("Pick")).not.toBeInTheDocument();
    expect(screen.queryByText("Providers")).not.toBeInTheDocument();
    expect(screen.queryByText("Plugins")).not.toBeInTheDocument();
    expect(screen.queryByText("The Movie Database")).not.toBeInTheDocument();
  });

  it("marks NSFW queue rows with a fire chip", () => {
    store.queue = [
      {
        id: "queue-1",
        entityId: "video-1",
        entityKind: "video",
        title: "Queued NSFW Movie",
        isNsfw: true,
        state: "proposal",
        provider: "tmdb",
        action: "search",
        candidates: [],
        proposal: { confidence: 0.91 },
        entity: {
          id: "video-1",
          kind: "video",
          title: "Queued NSFW Movie",
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
          isNsfw: true,
          isOrganized: false,
        },
      },
    ];

    render(IdentifyDashboard);

    expect(screen.getByLabelText("NSFW")).toBeInTheDocument();
    expect(screen.getByText("Queued NSFW Movie")).toBeInTheDocument();
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
    supports: [{ entityKind: "video", actions: ["search"] }],
    auth: [],
    missingAuthKeys: [],
  };
}
