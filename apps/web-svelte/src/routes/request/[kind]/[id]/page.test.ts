import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/svelte";
import { readFileSync } from "node:fs";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { page } from "$app/state";
import {
  ENTITY_KIND,
  EXTERNAL_ID_PROVIDER,
  MONITOR_PRESET,
  PROPOSAL_KIND,
  REQUEST_COMMIT_OUTCOME,
  REQUEST_MEDIA_KIND,
  REQUEST_REVIEW_SELECTION,
} from "$lib/api/generated/codes";
import type {
  BookAcquisitionProfileView,
  EntityMetadataProposal,
  LibraryRoot,
  RequestReviewResponse,
} from "$lib/api/generated/model";
import { ApiError } from "$lib/api/orval-fetch";
import { deriveRequestReviewSelection } from "$lib/requests/request-review-selection";
import Page from "./+page.svelte";

const mocks = vi.hoisted(() => ({
  commitReviewedRequest: vi.fn(),
  fetchAcquisitionProfiles: vi.fn(),
  fetchLibraryRoots: vi.fn(),
  goto: vi.fn(async () => {}),
  reviewRequest: vi.fn(),
}));

vi.mock("$lib/api/requests", () => ({
  commitReviewedRequest: mocks.commitReviewedRequest,
  reviewRequest: mocks.reviewRequest,
}));

vi.mock("$lib/api/acquisitions", async (importOriginal) => ({
  ...await importOriginal<typeof import("$lib/api/acquisitions")>(),
  fetchAcquisitionProfiles: mocks.fetchAcquisitionProfiles,
}));

vi.mock("$lib/api/settings", async (importOriginal) => ({
  ...await importOriginal<typeof import("$lib/api/settings")>(),
  fetchLibraryRoots: mocks.fetchLibraryRoots,
}));

vi.mock("$app/navigation", () => ({
  goto: mocks.goto,
  invalidate: vi.fn(async () => {}),
  invalidateAll: vi.fn(async () => {}),
}));

vi.mock("$lib/nsfw/store.svelte", () => ({
  useNsfw: () => ({ mode: "off" }),
}));

describe("reviewed request route", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    page.params = {};
    page.url = new URL("http://localhost/request") as unknown as typeof page.url;
    mocks.fetchLibraryRoots.mockResolvedValue([videoRoot()]);
    mocks.fetchAcquisitionProfiles.mockResolvedValue([tvProfile(), movieProfile()]);
    mocks.commitReviewedRequest.mockResolvedValue({ containerEntityId: null, items: [] });
  });

  afterEach(() => {
    cleanup();
  });

  it("loads the exact plugin and opaque external identity under the NSFW ceiling", async () => {
    const review = seriesReview();
    setRoute(
      REQUEST_MEDIA_KIND.series,
      "Show:AbC:01",
      `plugin=cinema-metadata&namespace=${EXTERNAL_ID_PROVIDER.tmdb}`,
    );
    mocks.reviewRequest.mockResolvedValue(review);

    render(Page);

    await screen.findByRole("heading", { name: "Andor" });
    expect(mocks.reviewRequest).toHaveBeenCalledWith({
      kind: REQUEST_MEDIA_KIND.series,
      pluginId: "cinema-metadata",
      externalIdentity: {
        namespace: EXTERNAL_ID_PROVIDER.tmdb,
        value: "Show:AbC:01",
      },
      hideNsfw: true,
    });
  });

  it("commits only selected direct children with the monitor preset and target choices", async () => {
    const review = seriesReview();
    setRoute(
      REQUEST_MEDIA_KIND.series,
      review.externalIdentity.value,
      `plugin=${review.pluginId}&namespace=${review.externalIdentity.namespace}`,
    );
    mocks.reviewRequest.mockResolvedValue(review);
    mocks.commitReviewedRequest.mockResolvedValue({
      containerEntityId: "series-entity",
      items: [{
        externalId: `${EXTERNAL_ID_PROVIDER.tmdb}:Show:AbC:01:1`,
        title: "Season 1",
        outcome: REQUEST_COMMIT_OUTCOME.requested,
        entityId: "season-entity",
        acquisitionId: "acquisition-1",
      }],
    });

    render(Page);

    await screen.findByText("TV Default");
    expect(screen.getAllByRole("checkbox")).toHaveLength(2);
    expect(screen.queryByRole("checkbox", { name: /Episode 1/ })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("checkbox", { name: "Deselect Season 2" }));
    await fireEvent.click(screen.getByRole("button", { name: "Request 1 season" }));

    await waitFor(() => {
      expect(mocks.commitReviewedRequest).toHaveBeenCalledWith({
        kind: REQUEST_MEDIA_KIND.series,
        pluginId: "cinema-metadata",
        rootExternalIdentity: {
          namespace: EXTERNAL_ID_PROVIDER.tmdb,
          value: "Show:AbC:01",
        },
        proposalRevision: "series-revision",
        selectedProposalIds: ["season-1"],
        targetLibraryRootId: "root-video",
        profileId: "profile-tv",
        preset: MONITOR_PRESET.missing,
      }, true);
    });
    expect(mocks.goto).toHaveBeenCalledWith("/request");
  });

  it("allows a future-only container monitor with no current child selection", async () => {
    const review = seriesReview();
    setRoute(
      REQUEST_MEDIA_KIND.series,
      review.externalIdentity.value,
      `plugin=${review.pluginId}&namespace=${review.externalIdentity.namespace}`,
    );
    mocks.reviewRequest.mockResolvedValue(review);
    mocks.commitReviewedRequest.mockResolvedValue({
      containerEntityId: "series-entity",
      items: [],
    });

    render(Page);

    await screen.findByText("TV Default");
    await fireEvent.click(screen.getByRole("button", { name: "Monitor" }));
    await fireEvent.click(
      within(screen.getByRole("listbox")).getByRole("option", { name: /future only/i }),
    );
    const requestButton = await screen.findByRole("button", { name: "Request" });
    expect(requestButton).toBeEnabled();
    await fireEvent.click(requestButton);

    await waitFor(() => {
      expect(mocks.commitReviewedRequest).toHaveBeenCalledWith(
        expect.objectContaining({
          selectedProposalIds: [],
          preset: MONITOR_PRESET.future,
        }),
        true,
      );
    });
    expect(mocks.goto).toHaveBeenCalledWith("/series/series-entity");
  });

  it("keeps a manually emptied custom selection invalid", async () => {
    const review = seriesReview();
    setRoute(
      REQUEST_MEDIA_KIND.series,
      review.externalIdentity.value,
      `plugin=${review.pluginId}&namespace=${review.externalIdentity.namespace}`,
    );
    mocks.reviewRequest.mockResolvedValue(review);

    render(Page);

    await screen.findByText("TV Default");
    await fireEvent.click(screen.getByRole("checkbox", { name: "Deselect Season 1" }));
    await fireEvent.click(screen.getByRole("checkbox", { name: "Deselect Season 2" }));

    expect(screen.getByRole("button", { name: "Request" })).toBeDisabled();
    expect(mocks.commitReviewedRequest).not.toHaveBeenCalled();
  });

  it("selects the root proposal for a leaf even when it carries non-target structural metadata", async () => {
    const review = movieReview();
    setRoute(
      REQUEST_MEDIA_KIND.movie,
      review.externalIdentity.value,
      `plugin=${review.pluginId}&namespace=${review.externalIdentity.namespace}`,
    );
    mocks.reviewRequest.mockResolvedValue(review);
    mocks.commitReviewedRequest.mockResolvedValue({
      containerEntityId: null,
      items: [{
        externalId: `${EXTERNAL_ID_PROVIDER.tmdb}:Movie:Part:Case`,
        title: "Arrival",
        outcome: REQUEST_COMMIT_OUTCOME.requested,
        entityId: "movie-entity",
        acquisitionId: "acquisition-movie",
      }],
    });

    render(Page);

    await screen.findByText("Movie Default");
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Request" }));

    await waitFor(() => {
      expect(mocks.commitReviewedRequest).toHaveBeenCalledWith({
        kind: REQUEST_MEDIA_KIND.movie,
        pluginId: "cinema-metadata",
        rootExternalIdentity: {
          namespace: EXTERNAL_ID_PROVIDER.tmdb,
          value: "Movie:Part:Case",
        },
        proposalRevision: "movie-revision",
        selectedProposalIds: ["movie-root"],
        targetLibraryRootId: "root-video",
        profileId: "profile-movie",
      }, true);
    });
    expect(mocks.goto).toHaveBeenCalledWith("/movies/movie-entity");
  });

  it("treats sibling volumes as direct selections even though a book is not a container kind", () => {
    const selection = deriveRequestReviewSelection(bookSiblingReview());

    expect(selection.mode).toBe(REQUEST_REVIEW_SELECTION.directChildren);
    expect(selection.selectableIds).toEqual(["book-volume-1", "book-volume-2"]);
    expect(selection.initialRootSelection).toEqual([]);
  });

  it("does not fall back to the root when child selection identities are incomplete", () => {
    const review = seriesReview();
    review.targets = review.targets.filter((target) => target.proposalId === review.proposal.proposalId);

    const selection = deriveRequestReviewSelection(review);

    expect(selection.mode).toBe(REQUEST_REVIEW_SELECTION.directChildren);
    expect(selection.selectableIds).toEqual([]);
    expect(selection.initialRootSelection).toEqual([]);
  });

  it("stops a stale commit and asks the user to reload the review", async () => {
    const review = movieReview();
    setRoute(
      REQUEST_MEDIA_KIND.movie,
      review.externalIdentity.value,
      `plugin=${review.pluginId}&namespace=${review.externalIdentity.namespace}`,
    );
    mocks.reviewRequest.mockResolvedValue(review);
    mocks.commitReviewedRequest.mockRejectedValue(new ApiError("Proposal changed", 409));

    render(Page);

    await screen.findByText("Movie Default");
    await fireEvent.click(screen.getByRole("button", { name: "Request" }));

    expect(await screen.findByText(/proposal changed after you reviewed it/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reload review" })).toBeInTheDocument();
    expect(mocks.goto).not.toHaveBeenCalled();
  });

  it("requires the plugin and namespace query contract", async () => {
    setRoute(REQUEST_MEDIA_KIND.movie, "Movie:Part:Case", "");

    render(Page);

    expect(await screen.findByText(/missing its plugin identity/i)).toBeInTheDocument();
    expect(mocks.reviewRequest).not.toHaveBeenCalled();
  });

  it("uses the shared proposal review and never restores synthetic entity detail adapters", () => {
    const source = readFileSync("src/routes/request/[kind]/[id]/+page.svelte", "utf8");

    expect(source).toContain("ProposalReviewSummary");
    expect(source).not.toContain("EntityDetail");
    expect(source).not.toContain("request-entity-card");
    expect(source).not.toContain("review-cards");
    expect(source).not.toContain("SelectableCardSection");
  });
});

function setRoute(kind: string, value: string, query: string) {
  page.params = { kind, id: value };
  page.url = new URL(
    `http://localhost/request/${kind}/${encodeURIComponent(value)}${query ? `?${query}` : ""}`,
  ) as unknown as typeof page.url;
}

function seriesReview(): RequestReviewResponse {
  const episode = proposal("episode-1", PROPOSAL_KIND.videoEpisode, "Episode 1", [], {
    seasonNumber: 1,
    episodeNumber: 1,
  });
  const seasonOne = proposal("season-1", PROPOSAL_KIND.videoSeason, "Season 1", [episode], { seasonNumber: 1 });
  const seasonTwo = proposal("season-2", PROPOSAL_KIND.videoSeason, "Season 2", [], { seasonNumber: 2 });
  const root = proposal("series-root", PROPOSAL_KIND.videoSeries, "Andor", [seasonOne, seasonTwo]);
  const externalIdentity = { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Show:AbC:01" };

  return {
    pluginId: "cinema-metadata",
    externalIdentity,
    entityKind: ENTITY_KIND.videoSeries,
    kind: REQUEST_MEDIA_KIND.series,
    proposal: root,
    revision: "series-revision",
    targets: [
      {
        proposalId: root.proposalId,
        kind: REQUEST_MEDIA_KIND.series,
        entityKind: ENTITY_KIND.videoSeries,
        externalIdentity,
        requestable: true,
      },
      {
        proposalId: seasonOne.proposalId,
        kind: REQUEST_MEDIA_KIND.season,
        entityKind: ENTITY_KIND.videoSeason,
        externalIdentity: { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Show:AbC:01:1" },
        requestable: true,
        position: 1,
      },
      {
        proposalId: seasonTwo.proposalId,
        kind: REQUEST_MEDIA_KIND.season,
        entityKind: ENTITY_KIND.videoSeason,
        externalIdentity: { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Show:AbC:01:2" },
        requestable: true,
        position: 2,
      },
      {
        proposalId: episode.proposalId,
        kind: REQUEST_MEDIA_KIND.episode,
        entityKind: ENTITY_KIND.video,
        externalIdentity: { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Show:AbC:01:1:1" },
        requestable: true,
        position: 1,
      },
    ],
  };
}

function movieReview(): RequestReviewResponse {
  const trailer = proposal("trailer-metadata", PROPOSAL_KIND.video, "Official Trailer");
  const root = proposal("movie-root", PROPOSAL_KIND.movie, "Arrival", [trailer]);
  const externalIdentity = { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Movie:Part:Case" };
  return {
    pluginId: "cinema-metadata",
    externalIdentity,
    entityKind: ENTITY_KIND.movie,
    kind: REQUEST_MEDIA_KIND.movie,
    proposal: root,
    revision: "movie-revision",
    targets: [{
      proposalId: root.proposalId,
      kind: REQUEST_MEDIA_KIND.movie,
      entityKind: ENTITY_KIND.movie,
      externalIdentity,
      requestable: true,
    }],
  };
}

function bookSiblingReview(): RequestReviewResponse {
  const volumeOne = proposal("book-volume-1", PROPOSAL_KIND.book, "Volume 1");
  const volumeTwo = proposal("book-volume-2", PROPOSAL_KIND.book, "Volume 2");
  const root = proposal("book-series-root", PROPOSAL_KIND.book, "A Book Series", [volumeOne, volumeTwo]);
  const externalIdentity = { namespace: "openlibrary", value: "works/OL:Series:Case" };
  return {
    pluginId: "open-library",
    externalIdentity,
    entityKind: ENTITY_KIND.book,
    kind: REQUEST_MEDIA_KIND.book,
    proposal: root,
    revision: "book-revision",
    targets: [
      {
        proposalId: root.proposalId,
        kind: REQUEST_MEDIA_KIND.book,
        entityKind: ENTITY_KIND.book,
        externalIdentity,
        requestable: true,
      },
      {
        proposalId: volumeOne.proposalId,
        kind: REQUEST_MEDIA_KIND.book,
        entityKind: ENTITY_KIND.book,
        externalIdentity: { namespace: "openlibrary", value: "works/OL:Volume:1" },
        requestable: true,
        position: 1,
      },
      {
        proposalId: volumeTwo.proposalId,
        kind: REQUEST_MEDIA_KIND.book,
        entityKind: ENTITY_KIND.book,
        externalIdentity: { namespace: "openlibrary", value: "works/OL:Volume:2" },
        requestable: true,
        position: 2,
      },
    ],
  };
}

function proposal(
  proposalId: string,
  targetKind: EntityMetadataProposal["targetKind"],
  title: string,
  children: EntityMetadataProposal[] = [],
  positions: Record<string, number> = {},
): EntityMetadataProposal {
  return {
    proposalId,
    provider: "cinema-metadata",
    targetKind,
    confidence: 1,
    matchReason: "external-id",
    patch: {
      title,
      description: `${title} description`,
      externalIds: {},
      urls: [],
      tags: [],
      studio: null,
      credits: [],
      dates: {},
      stats: {},
      positions,
      classification: null,
    },
    images: [],
    children,
    relationships: [],
    candidates: [],
  };
}

function videoRoot(): LibraryRoot {
  return {
    id: "root-video",
    path: "/media/video",
    label: "Video Library",
    enabled: true,
    recursive: true,
    scanVideos: true,
    scanImages: false,
    scanAudio: false,
    scanBooks: false,
    isNsfw: false,
    lastScannedAt: null,
    createdAt: "2026-07-09T00:00:00Z",
    updatedAt: "2026-07-09T00:00:00Z",
  };
}

function tvProfile(): BookAcquisitionProfileView {
  return {
    id: "profile-tv",
    kind: ENTITY_KIND.videoSeries,
    displayName: "TV Default",
    isDefault: true,
    targetLibraryRootId: "root-video",
  } as BookAcquisitionProfileView;
}

function movieProfile(): BookAcquisitionProfileView {
  return {
    id: "profile-movie",
    kind: ENTITY_KIND.movie,
    displayName: "Movie Default",
    isDefault: true,
    targetLibraryRootId: "root-video",
  } as BookAcquisitionProfileView;
}
