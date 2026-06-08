import { fireEvent, render, screen } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { EntityMetadataPatch, EntityMetadataProposal } from "$lib/api/identify-types";
import type { EntityCard as EntityDetailCard, EntityThumbnail as EntityCard } from "$lib/api/generated/model";
import IdentifyReviewChild from "./IdentifyReviewChild.svelte";
import IdentifyReviewParent from "./IdentifyReviewParent.svelte";

const store = vi.hoisted(() => ({
  applying: false,
  applyProgress: null as null | {
    id: string;
    entityId: string;
    state: string;
    currentIndex: number;
    total: number;
    currentKind?: string | null;
    currentTitle?: string | null;
    currentPath: string[];
    error?: string | null;
    updatedAt: string;
  },
  reviewCascadeSelections: {},
  reviewFieldSelections: {},
  reviewImageSelections: {},
  reviewTagSelections: {},
  queue: [] as unknown[],
  beginProposalReview: vi.fn(),
  getReviewDetailForProposal: vi.fn(),
  getReviewFieldSelections: vi.fn(),
  getReviewImageSelections: vi.fn(),
  getReviewTagSelections: vi.fn(),
  isReviewProposalSelected: vi.fn(),
  navigateTo: vi.fn(),
  navigateToDashboard: vi.fn(),
  nextQueueItem: vi.fn(),
  reviewDetailEntityIdForProposal: vi.fn(),
  setReviewFieldSelections: vi.fn(),
  setReviewImageSelections: vi.fn(),
  setReviewProposalSelected: vi.fn(),
  setReviewTagSelections: vi.fn(),
  setReviewTagSelected: vi.fn(),
  ensureReviewDetailForProposal: vi.fn(),
  deleteQueueItem: vi.fn(),
  rejectQueueItem: vi.fn(),
  applyProposal: vi.fn(),
  cascadeRunning: vi.fn(() => false),
  ensureCascadePoll: vi.fn(),
  stopCascadePoll: vi.fn(),
}));

vi.mock("./identify-store.svelte", () => ({
  useIdentifyStore: () => store,
}));

describe("Identify review surfaces", () => {
  beforeEach(() => {
    store.applying = false;
    store.applyProgress = null;
    store.queue = [];
    store.beginProposalReview.mockReset();
    store.getReviewDetailForProposal.mockReset();
    store.getReviewFieldSelections.mockReset();
    store.getReviewImageSelections.mockReset();
    store.getReviewTagSelections.mockReset();
    store.isReviewProposalSelected.mockReset();
    store.isReviewProposalSelected.mockReturnValue(true);
    store.navigateTo.mockReset();
    store.navigateToDashboard.mockReset();
    store.nextQueueItem.mockReset();
    store.nextQueueItem.mockReturnValue(null);
    store.reviewDetailEntityIdForProposal.mockReset();
    store.reviewDetailEntityIdForProposal.mockReturnValue(null);
    store.setReviewFieldSelections.mockReset();
    store.setReviewImageSelections.mockReset();
    store.setReviewProposalSelected.mockReset();
    store.setReviewTagSelections.mockReset();
    store.setReviewTagSelected.mockReset();
    store.ensureReviewDetailForProposal.mockReset();
    store.deleteQueueItem.mockReset();
    store.rejectQueueItem.mockReset();
    store.applyProposal.mockReset();
  });

  it("renders parent review relationship thumbnails with the normal card variant", () => {
    const { container } = render(IdentifyReviewParent, {
      props: {
        entity: entity(),
        proposal: proposal("root", {
          relationships: [proposal("person-1", { targetKind: "person", title: "Tim Robinson" })],
          children: [proposal("episode-1", { targetKind: "video", title: "Episode 1" })],
        }),
      },
    });

    const thumbnails = container.querySelectorAll(".entity-thumbnail");
    expect(thumbnails.length).toBeGreaterThan(0);
    expect(container.querySelector(".entity-thumbnail.is-list")).toBeNull();
  });

  it("renders walked child relationship thumbnails with the normal card variant", () => {
    const parentProposal = proposal("root");
    const { container } = render(IdentifyReviewChild, {
      props: {
        entity: entity(),
        parentProposal,
        proposal: proposal("episode-1", {
          targetKind: "video",
          relationships: [proposal("person-1", { targetKind: "person", title: "Tim Robinson" })],
          children: [proposal("clip-1", { targetKind: "video", title: "Clip 1" })],
        }),
      },
    });

    const thumbnails = container.querySelectorAll(".entity-thumbnail");
    expect(thumbnails.length).toBeGreaterThan(0);
    expect(container.querySelector(".entity-thumbnail.is-list")).toBeNull();
  });

  it("renders poster and backdrop artwork as enlarged review groups", () => {
    const { container } = render(IdentifyReviewParent, {
      props: {
        entity: entity(),
        proposal: proposal("root", {
          images: [
            image("poster", "poster-1"),
            image("poster", "poster-2"),
            image("backdrop", "backdrop-1"),
            image("backdrop", "backdrop-2"),
          ],
        }),
      },
    });

    const posterGroup = container.querySelector<HTMLElement>("[data-artwork-kind='poster']");
    const backdropGroup = container.querySelector<HTMLElement>("[data-artwork-kind='backdrop']");

    expect(posterGroup?.classList.contains("identify-artwork-grid")).toBe(true);
    expect(backdropGroup?.classList.contains("identify-artwork-grid")).toBe(true);
    expect(posterGroup?.querySelectorAll(".identify-artwork-tile")).toHaveLength(2);
    expect(backdropGroup?.querySelectorAll(".identify-artwork-tile")).toHaveLength(2);
    for (const img of container.querySelectorAll(".identify-artwork-tile img")) {
      expect(img).toHaveAttribute("referrerpolicy", "no-referrer");
    }
  });

  it("labels the field diff panel as base fields and collapses panel content from the header", async () => {
    render(IdentifyReviewParent, {
      props: {
        entity: entity(),
        proposal: proposal("root"),
      },
    });

    expect(screen.queryByText("Field diff")).not.toBeInTheDocument();
    const header = screen.getByRole("button", { name: /Base fields/ });
    expect(header).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText("Current")).toBeInTheDocument();

    await fireEvent.click(header);

    expect(header).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByText("Current")).not.toBeInTheDocument();
  });

  it("keeps base field actions from toggling the section", async () => {
    render(IdentifyReviewParent, {
      props: {
        entity: entity(),
        proposal: proposal("root"),
      },
    });

    const header = screen.getByRole("button", { name: /Base fields/ });
    await fireEvent.click(screen.getByRole("button", { name: "None" }));

    expect(header).toHaveAttribute("aria-expanded", "true");
  });

  it("shows New and Merge chips on relationship thumbnails", () => {
    render(IdentifyReviewParent, {
      props: {
        entity: entity(),
        detail: detail({
          relationships: [
            {
              kind: "person",
              label: "Credits",
              code: "cast",
              entities: [entity({ id: "person-1", kind: "person", title: "Tim Robinson" })],
            },
          ],
        }),
        proposal: proposal("root", {
          relationships: [
            proposal("person-existing", { targetKind: "person", title: "Tim Robinson" }),
            proposal("person-new", { targetKind: "person", title: "New Actor" }),
          ],
        }),
      },
    });

    expect(screen.getAllByText("Merge").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("New").length).toBeGreaterThanOrEqual(1);
  });

  it("renders a matched child with its local cover and provider title", () => {
    render(IdentifyReviewParent, {
      props: {
        entity: entity(),
        detail: detail({
          childrenByKind: [
            {
              kind: "video",
              label: "Episodes",
              entities: [
                entity({
                  id: "episode-1",
                  kind: "video",
                  title: "Local Episode One",
                  coverUrl: "/assets/thumbnails/episode-1.jpg",
                }),
              ],
            },
          ],
        }),
        proposal: proposal("root", {
          children: [
            proposal("episode-1-proposal", {
              targetKind: "video-episode",
              title: "Episode One",
              targetEntityId: "episode-1",
              images: [],
              patch: {
                positions: { seasonNumber: 1, episodeNumber: 1 },
              },
            }),
          ],
        }),
      },
    });

    expect(screen.getByText("Episode One")).toBeInTheDocument();
    expect(screen.getByAltText("Local Episode One")).toBeInTheDocument();
  });

  it("shows local covers for walked child grandchildren when provider stills are missing", () => {
    store.getReviewDetailForProposal.mockReturnValue(detail({
      id: "season-1",
      kind: "video-season",
      childrenByKind: [
        {
          kind: "video",
          label: "Episodes",
          entities: [
            entity({
              id: "episode-1",
              kind: "video",
              title: "Local Episode One",
              coverUrl: "/assets/thumbnails/episode-1.jpg",
            }),
          ],
        },
      ],
    }));

    render(IdentifyReviewChild, {
      props: {
        entity: entity(),
        parentProposal: proposal("root"),
        proposal: proposal("season-1-proposal", {
          targetKind: "video-season",
          title: "Season 1",
          targetEntityId: "season-1",
          children: [
            proposal("episode-1-proposal", {
              targetKind: "video-episode",
              title: "Episode One",
              targetEntityId: "episode-1",
              images: [],
              patch: {
                positions: { seasonNumber: 1, episodeNumber: 1 },
              },
            }),
          ],
        }),
      },
    });

    expect(screen.getByText("E01")).toBeInTheDocument();
    expect(screen.getByAltText("Local Episode One")).toHaveAttribute("src", "/assets/thumbnails/episode-1.jpg");
  });

  it("renders a full-width apply progress row with the active proposal path", () => {
    store.applying = true;
    store.applyProgress = {
      id: "apply-1",
      entityId: "entity-1",
      state: "running",
      currentIndex: 3,
      total: 12,
      currentKind: "video",
      currentTitle: "Episode 3",
      currentPath: ["The Chair Company", "Season 1", "Episode 3"],
      error: null,
      updatedAt: new Date().toISOString(),
    };

    render(IdentifyReviewParent, {
      props: {
        entity: entity(),
        proposal: proposal("root"),
      },
    });

    expect(screen.getByText("Applying metadata")).toBeInTheDocument();
    expect(screen.getByText("3/12")).toBeInTheDocument();
    expect(screen.getAllByText("The Chair Company").length).toBeGreaterThan(0);
    expect(screen.getByText("Season 1")).toBeInTheDocument();
    expect(screen.getByText("Episode 3")).toBeInTheDocument();
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "25");
  });

  it("places reject actions before accept actions and advances on reject-and-next", async () => {
    store.nextQueueItem.mockReturnValue({ entityId: "entity-2" });

    render(IdentifyReviewParent, {
      props: {
        entity: entity(),
        proposal: proposal("root"),
      },
    });

    const actions = screen.getByTestId("identify-proposal-actions");
    expect(actions).toHaveTextContent(/Reject.*Reject and Next.*Accept.*Accept and Next/);

    await fireEvent.click(screen.getByRole("button", { name: "Reject" }));
    expect(store.rejectQueueItem).toHaveBeenCalledWith("entity-1");

    await fireEvent.click(screen.getByRole("button", { name: "Reject and Next" }));
    expect(store.rejectQueueItem).toHaveBeenCalledWith("entity-1", { navigateNext: true });
  });
});

function entity(overrides: Partial<EntityCard> = {}): EntityCard {
  return {
    id: overrides.id ?? "entity-1",
    kind: overrides.kind ?? "video-series",
    title: overrides.title ?? "The Chair Company",
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

function detail(overrides: Partial<EntityDetailCard> = {}): EntityDetailCard {
  return {
    id: "entity-1",
    kind: "video-series",
    title: "The Chair Company",
    parentEntityId: null,
    sortOrder: null,
    capabilities: [],
    childrenByKind: [],
    relationships: [],
    ...overrides,
  };
}

function proposal(
  proposalId: string,
  overrides: Omit<Partial<EntityMetadataProposal>, "patch"> & { title?: string; patch?: Partial<EntityMetadataPatch> } = {},
): EntityMetadataProposal {
  const {
    title = "The Chair Company",
    patch,
    ...proposalOverrides
  } = overrides;

  return {
    proposalId,
    provider: "tmdb",
    targetKind: "video-series",
    confidence: 1,
    matchReason: "external-id",
    patch: {
      title,
      description: "A proposal description.",
      externalIds: {},
      urls: [],
      tags: [],
      studio: null,
      credits: [],
      dates: {},
      stats: {},
      positions: {},
      classification: null,
      ...patch,
    } satisfies EntityMetadataPatch,
    images: [{
      kind: "poster",
      url: `https://image.tmdb.org/t/p/original/${proposalId}.jpg`,
      source: "tmdb",
    }],
    children: [],
    relationships: [],
    candidates: [],
    ...proposalOverrides,
  };
}

function image(kind: string, name: string) {
  return {
    kind,
    url: `https://image.tmdb.org/t/p/original/${name}.jpg`,
    source: "tmdb",
  };
}
