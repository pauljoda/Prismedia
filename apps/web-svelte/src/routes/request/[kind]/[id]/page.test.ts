import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { page } from "$app/state";
import {
  ENTITY_KIND,
  EXTERNAL_ID_PROVIDER,
  MONITOR_PRESET,
  PROBLEM_CODE,
  REQUEST_COMMIT_OUTCOME,
  REQUEST_MEDIA_KIND,
  REQUEST_REVIEW_SELECTION,
  CONNECTION_STATUS, INTEGRATION_OPERATION, PLUGIN_CAPABILITY,
  CREDIT_ROLE, METADATA_PATCH_FIELD,
} from "$lib/api/generated/codes";
import type {
  BookAcquisitionProfileView,
  EntityMetadataProposal,
  LibraryRootSummary,
  RequestReviewResponse,
} from "$lib/api/generated/model";
import { ApiError } from "$lib/api/orval-fetch";
import { deriveRequestReviewSelection } from "$lib/requests/request-review-selection";
import Page from "./+page.svelte";

const mocks = vi.hoisted(() => ({
  commitReviewedRequest: vi.fn(),
  fetchRequestReview: vi.fn(),
  fetchAcquisitionProfiles: vi.fn(),
  fetchAccessibleLibraryRoots: vi.fn(),
  goto: vi.fn(async () => {}),
  reviewRequest: vi.fn(),
  reviewManagerTitle: vi.fn(), prepareManagerTitle: vi.fn(),
  prepareManagedMovie: vi.fn(), prepareManagedSeries: vi.fn(), fetchConnections: vi.fn(), fetchManagedRequests: vi.fn(), fetchLibraryMounts: vi.fn(), isAdmin: false,
}));

vi.mock("$lib/api/requests", () => ({
  commitReviewedRequest: mocks.commitReviewedRequest,
  fetchRequestReview: mocks.fetchRequestReview,
  reviewRequest: mocks.reviewRequest,
  prepareManagedMovie: mocks.prepareManagedMovie,
  prepareManagedSeries: mocks.prepareManagedSeries,
}));
vi.mock("$lib/api/connections", () => ({ fetchConnections: mocks.fetchConnections }));
vi.mock("$lib/api/managed-discovery", () => ({ reviewManagerTitle: mocks.reviewManagerTitle, prepareManagerTitle: mocks.prepareManagerTitle }));
vi.mock("$lib/stores/app-chrome.svelte", () => ({ useAppChrome: () => ({ setBreadcrumbs: () => () => {} }) }));
vi.mock("$lib/api/managed-requests", () => ({ fetchManagedRequests: mocks.fetchManagedRequests }));
vi.mock("$lib/api/managed-libraries", () => ({ fetchLibraryMounts: mocks.fetchLibraryMounts }));

vi.mock("$lib/api/acquisitions", async (importOriginal) => ({
  ...await importOriginal<typeof import("$lib/api/acquisitions")>(),
  fetchAcquisitionProfiles: mocks.fetchAcquisitionProfiles,
}));

vi.mock("$lib/api/settings", async (importOriginal) => ({
  ...await importOriginal<typeof import("$lib/api/settings")>(),
  fetchAccessibleLibraryRoots: mocks.fetchAccessibleLibraryRoots,
}));

vi.mock("$app/navigation", () => ({
  goto: mocks.goto,
  invalidate: vi.fn(async () => {}),
  invalidateAll: vi.fn(async () => {}),
}));

vi.mock("$lib/nsfw/store.svelte", () => ({
  useNsfw: () => ({ mode: "off" }),
}));

vi.mock("$lib/stores/session.svelte", () => ({
  useSession: () => ({ canRequestContent: true, isAdmin: mocks.isAdmin }),
}));

describe("reviewed request route", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.isAdmin = false;
    mocks.fetchConnections.mockResolvedValue([]);
    mocks.fetchManagedRequests.mockResolvedValue([]);
    mocks.fetchLibraryMounts.mockResolvedValue([]);
    page.params = {};
    page.url = new URL("http://localhost/request") as unknown as typeof page.url;
    mocks.fetchAccessibleLibraryRoots.mockResolvedValue([videoRoot()]);
    mocks.fetchAcquisitionProfiles.mockResolvedValue([tvProfile(), movieProfile()]);
    mocks.commitReviewedRequest.mockResolvedValue({ containerEntityId: null, items: [] });
  });

  afterEach(() => {
    cleanup();
  });

  it("keeps a manager-originated review and its metadata preparation on the same connection", async () => {
    mocks.isAdmin = true;
    const review = movieReview();
    const connection = { id: "manager", name: "Selected movie manager", enabled: true, status: CONNECTION_STATUS.ready,
      effectiveCapabilities: [{ kind: PLUGIN_CAPABILITY.externalManager, entityKinds: [ENTITY_KIND.movie],
        operations: [INTEGRATION_OPERATION.lookupManaged, INTEGRATION_OPERATION.ensureManaged] }] };
    mocks.fetchConnections.mockResolvedValue([connection]);
    mocks.reviewManagerTitle.mockResolvedValue({ connectionRevision: 7, review });
    mocks.prepareManagerTitle.mockResolvedValue({ entityId: "wanted-movie", title: "Prepared movie", hasFile: false });
    setRoute(REQUEST_MEDIA_KIND.movie, review.externalIdentity.value, `connection=manager&namespace=${EXTERNAL_ID_PROVIDER.tmdb}`);
    render(Page);
    await screen.findByText("Request through Selected movie manager");
    expect(mocks.reviewManagerTitle).toHaveBeenCalledWith("manager", { entityKind: ENTITY_KIND.movie, externalIdentity: review.externalIdentity });
    expect(mocks.reviewRequest).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Acquisition owner" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Request$/ })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Save metadata and review manager request" }));
    await screen.findByText(/Metadata saved/);
    expect(mocks.prepareManagerTitle).toHaveBeenCalledWith("manager", expect.objectContaining({
      connectionRevision: 7, request: expect.objectContaining({ rootExternalIdentity: review.externalIdentity, proposalRevision: review.revision }),
    }));
    expect(mocks.prepareManagedMovie).not.toHaveBeenCalled();
    expect(mocks.commitReviewedRequest).not.toHaveBeenCalled();
  });

  it("prepares the reviewed movie for the selected manager without sending a native request", async () => {
    mocks.isAdmin = true;
    const review = movieReview();
    mocks.reviewRequest.mockResolvedValue(review);
    mocks.fetchConnections.mockResolvedValue([{ id: "manager", name: "Movie manager", enabled: true, status: CONNECTION_STATUS.ready,
      effectiveCapabilities: [{ kind: PLUGIN_CAPABILITY.externalManager, entityKinds: [ENTITY_KIND.movie],
        operations: [INTEGRATION_OPERATION.lookupManaged, INTEGRATION_OPERATION.ensureManaged] }] }]);
    mocks.prepareManagedMovie.mockResolvedValue({ entityId: "wanted-movie", title: "Prepared movie", hasFile: false });
    setRoute(REQUEST_MEDIA_KIND.movie, review.externalIdentity.value, `plugin=${review.pluginId}&namespace=${EXTERNAL_ID_PROVIDER.tmdb}`);
    render(Page);
    await fireEvent.keyDown(await screen.findByRole("button", { name: "Acquisition owner" }), { key: "ArrowDown" });
    await fireEvent.pointerUp(await screen.findByRole("option", { name: "Movie manager" }));
    expect(mocks.prepareManagedMovie).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: /^Request$/ })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Save metadata and review manager request" }));
    await screen.findByText(/Metadata saved/);
    expect(mocks.prepareManagedMovie).toHaveBeenCalledWith(expect.objectContaining({ review, rootExternalIdentity: review.externalIdentity, pluginId: review.pluginId }));
    expect(mocks.commitReviewedRequest).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Acquisition owner" })).toBeDisabled();
    expect(screen.getByText("Prepared movie")).toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: "Accept Title" })).not.toBeInTheDocument();
  });

  it("prepares only hydrated episodes retained by season and nested episode review", async () => {
    mocks.isAdmin = true;
    const review = seriesReview();
    const seasonOne = review.proposal.children[0] as EntityMetadataProposal;
    const seasonTwo = review.proposal.children[1] as EntityMetadataProposal;
    const episodeThree = proposal("episode-3", ENTITY_KIND.videoEpisode, "Episode 3", [], {
      seasonNumber: 1,
      episodeNumber: 3,
    });
    const episodeFour = proposal("episode-4", ENTITY_KIND.videoEpisode, "Episode 4", [], {
      seasonNumber: 1,
      episodeNumber: 4,
    });
    const episodeTwo = proposal("episode-2", ENTITY_KIND.videoEpisode, "Episode 2", [], {
      seasonNumber: 2,
      episodeNumber: 2,
    });
    seasonOne.children.push(episodeThree);
    seasonOne.children.push(episodeFour);
    seasonTwo.children.push(episodeTwo);
    review.proposal.patch.externalIds = { [EXTERNAL_ID_PROVIDER.tmdb]: review.externalIdentity.value };
    seasonOne.patch.externalIds = { [EXTERNAL_ID_PROVIDER.tmdb]: "Show:AbC:01:1" };
    seasonTwo.patch.externalIds = { [EXTERNAL_ID_PROVIDER.tmdb]: "Show:AbC:01:2" };
    seasonOne.children[0]!.patch.externalIds = { [EXTERNAL_ID_PROVIDER.tmdb]: "Show:AbC:01:1:1" };
    episodeThree.patch.externalIds = { [EXTERNAL_ID_PROVIDER.tmdb]: "Show:AbC:01:1:3" };
    episodeFour.patch.externalIds = { [EXTERNAL_ID_PROVIDER.tmdb]: "Show:AbC:01:1:4" };
    episodeTwo.patch.externalIds = { [EXTERNAL_ID_PROVIDER.tmdb]: "Show:AbC:01:2:2" };
    review.targets.push(
      {
        proposalId: episodeThree.proposalId,
        kind: REQUEST_MEDIA_KIND.episode,
        entityKind: ENTITY_KIND.videoEpisode,
        externalIdentity: { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Show:AbC:01:1:3" },
        requestable: true,
        position: 3,
      },
      {
        proposalId: episodeTwo.proposalId,
        kind: REQUEST_MEDIA_KIND.episode,
        entityKind: ENTITY_KIND.videoEpisode,
        externalIdentity: { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Show:AbC:01:2:2" },
        requestable: true,
        position: 2,
      },
      {
        proposalId: episodeFour.proposalId,
        kind: REQUEST_MEDIA_KIND.episode,
        entityKind: ENTITY_KIND.videoEpisode,
        externalIdentity: { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Show:AbC:01:1:4" },
        requestable: true,
        position: 4,
      },
    );
    mocks.reviewRequest.mockResolvedValue(review);
    mocks.fetchConnections.mockResolvedValue([{ id: "manager", name: "Series manager", enabled: true, status: CONNECTION_STATUS.ready,
      effectiveCapabilities: [{ kind: PLUGIN_CAPABILITY.externalManager, entityKinds: [ENTITY_KIND.videoSeries],
        operations: [INTEGRATION_OPERATION.lookupManaged, INTEGRATION_OPERATION.ensureManaged] }] }]);
    mocks.prepareManagedSeries.mockResolvedValue({
      seriesEntityId: "wanted-series",
      title: "Andor",
      episodes: [
        {
          entityId: "owned-episode-4",
          seasonEntityId: "wanted-season-1",
          title: "Episode 4",
          seasonNumber: 1,
          episodeNumber: 4,
          absoluteNumber: null,
          hasFile: true,
          externalIdentity: { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Show:AbC:01:1:4" },
        },
        {
          entityId: "wanted-episode-3",
          seasonEntityId: "wanted-season-1",
          title: "Episode 3",
          seasonNumber: 1,
          episodeNumber: 3,
          absoluteNumber: null,
          hasFile: false,
          externalIdentity: { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Show:AbC:01:1:3" },
        },
      ],
    });
    setRoute(REQUEST_MEDIA_KIND.series, review.externalIdentity.value, `plugin=${review.pluginId}&namespace=${review.externalIdentity.namespace}`);
    render(Page);

    await screen.findByRole("heading", { name: "Andor" });
    await fireEvent.click(screen.getByRole("checkbox", { name: "Deselect Season 2" }));
    await fireEvent.click(screen.getByRole("button", { name: "Review Season 1" }));
    await fireEvent.click(screen.getByRole("checkbox", { name: "Deselect Episode 1" }));
    await fireEvent.keyDown(screen.getByRole("button", { name: "Acquisition owner" }), { key: "ArrowDown" });
    await fireEvent.pointerUp(await screen.findByRole("option", { name: "Series manager" }));
    expect(screen.getAllByText("Request selected episodes").length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: "Monitor" })).not.toBeInTheDocument();
    expect(screen.queryByText("All current and future")).not.toBeInTheDocument();
    expect(screen.getAllByText(/Choose seasons and episodes in the metadata review/).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Back to Andor" })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Save metadata and review manager request" }));

    await waitFor(() => expect(mocks.prepareManagedSeries).toHaveBeenCalledWith(expect.objectContaining({
      selectedProposalIds: ["episode-3", "episode-4"],
      proposal: expect.objectContaining({
        children: [expect.objectContaining({
          proposalId: "season-1",
          children: [
            expect.objectContaining({ proposalId: "episode-3" }),
            expect.objectContaining({ proposalId: "episode-4" }),
          ],
        })],
      }),
    })));
    expect(mocks.prepareManagedMovie).not.toHaveBeenCalled();
    expect(screen.getByText(/1 already-owned episode is omitted/)).toBeInTheDocument();
    expect(await screen.findByText("S01E03")).toBeInTheDocument();
    expect(screen.queryByText("S01E04")).not.toBeInTheDocument();
    expect(screen.getByText("Episode 3")).toBeInTheDocument();
    expect(screen.queryAllByText("Episode selection")).toHaveLength(0);
    expect(screen.queryAllByText(/Choose seasons and episodes in the metadata review/)).toHaveLength(0);
    expect(screen.queryByRole("button", { name: "Back to Andor" })).not.toBeInTheDocument();
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

  it("shows the shallow proposal immediately and identifies child cards until enrichment finishes", async () => {
    const completed = seriesReview();
    const reviewId = "11111111-1111-1111-1111-111111111111";
    const initial = {
      ...completed,
      proposal: {
        ...completed.proposal,
        children: completed.proposal.children.map((child) => ({ ...child, children: [] })),
      },
      targets: completed.targets.filter((target) => target.proposalId !== "episode-1"),
      enrichment: {
        reviewId,
        running: true,
        pendingProposalIds: ["season-1", "season-2"],
        error: null,
        updatedAt: "2026-08-09T20:00:00Z",
      },
    } as RequestReviewResponse;
    let finishEnrichment!: (value: RequestReviewResponse) => void;
    mocks.reviewRequest.mockResolvedValue(initial);
    mocks.fetchRequestReview
      .mockRejectedValueOnce(new Error("Temporary refresh failure"))
      .mockImplementationOnce(() => new Promise((resolve) => {
        finishEnrichment = resolve;
      }));
    setRoute(
      REQUEST_MEDIA_KIND.series,
      initial.externalIdentity.value,
      `plugin=${initial.pluginId}&namespace=${initial.externalIdentity.namespace}`,
    );

    render(Page);

    await screen.findByRole("heading", { name: "Andor" });
    expect(screen.getAllByText("Identifying…").length).toBeGreaterThan(0);
    expect(screen.getAllByRole("button", { name: "Request 2 seasons" })[0]).toBeDisabled();
    expect(mocks.fetchRequestReview).toHaveBeenCalledWith(reviewId);
    expect((await screen.findAllByText("Temporary refresh failure")).length).toBeGreaterThan(0);
    await waitFor(() => expect(mocks.fetchRequestReview).toHaveBeenCalledTimes(2), {
      timeout: 2_500,
    });

    finishEnrichment({
      ...completed,
      enrichment: {
        ...initial.enrichment,
        running: false,
        pendingProposalIds: [],
        updatedAt: "2026-08-09T20:00:02Z",
      },
    } as RequestReviewResponse);

    await waitFor(() => {
      expect(screen.queryByText("Identifying…")).not.toBeInTheDocument();
      expect(screen.getAllByRole("button", { name: "Request 2 seasons" })[0]).toBeEnabled();
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

    await screen.findAllByText("TV Default");
    expect(screen.getByRole("checkbox", { name: "Deselect Season 1" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Deselect Season 2" })).toBeChecked();
    expect(screen.queryByRole("checkbox", { name: /Episode 1/ })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("checkbox", { name: "Deselect Season 2" }));
    await fireEvent.click(screen.getAllByRole("button", { name: "Request 1 season" })[0]);

    await waitFor(() => {
      expect(mocks.commitReviewedRequest).toHaveBeenCalledWith(expect.objectContaining({
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
        preset: MONITOR_PRESET.all,
        review,
        proposal: expect.objectContaining({
          proposalId: review.proposal.proposalId,
          children: [expect.objectContaining({ proposalId: "season-1" })],
        }),
        selectedFields: expect.arrayContaining(["title", "description"]),
        selectedImages: {},
      }), true);
    });
    expect(mocks.goto).toHaveBeenCalledWith("/series/series-entity");
  });

  it("reviews direct credits through All/None and omits rejected credits from the request", async () => {
    const review = movieReview();
    review.proposal.patch.credits = [{ name: "Direct provider credit", role: CREDIT_ROLE.actor, character: null, sortOrder: 0 }];
    mocks.reviewRequest.mockResolvedValue(review);
    setRoute(REQUEST_MEDIA_KIND.movie, review.externalIdentity.value, `plugin=${review.pluginId}&namespace=${review.externalIdentity.namespace}`);
    render(Page);
    const credits = await screen.findByRole("checkbox", { name: "Accept Credits" });
    expect(credits).toBeChecked();
    await fireEvent.click(screen.getByRole("button", { name: "None" }));
    expect(credits).not.toBeChecked();
    await fireEvent.click(screen.getByRole("button", { name: "All" }));
    expect(credits).toBeChecked();
    await fireEvent.click(credits);
    await fireEvent.click(screen.getAllByRole("button", { name: "Request" })[0]);
    await waitFor(() => expect(mocks.commitReviewedRequest).toHaveBeenCalled());
    const payload = mocks.commitReviewedRequest.mock.calls[0][0];
    expect(payload.selectedFields).not.toContain(METADATA_PATCH_FIELD.credits);
    expect(payload.proposal.patch.credits).toEqual([]);
  });

  it("uses the identify metadata controls and sends the cached filtered proposal", async () => {
    const review = movieReview();
    const person = proposal("person-amy", ENTITY_KIND.person, "Amy Adams");
    review.proposal.patch.tags = ["Drama", "Science Fiction"];
    review.proposal.patch.credits = [{
      name: "Amy Adams",
      role: "actor",
      character: "Louise Banks",
      sortOrder: 1,
    }];
    review.proposal.images = [
      {
        kind: "poster",
        url: "https://images.test/one.jpg",
        source: "tmdb",
        rank: 1,
        language: null,
        width: 1000,
        height: 1500,
      },
      {
        kind: "poster",
        url: "https://images.test/two.jpg",
        source: "tmdb",
        rank: 2,
        language: null,
        width: 1000,
        height: 1500,
      },
    ];
    review.proposal.relationships = [person];
    setRoute(
      REQUEST_MEDIA_KIND.movie,
      review.externalIdentity.value,
      `plugin=${review.pluginId}&namespace=${review.externalIdentity.namespace}`,
    );
    mocks.reviewRequest.mockResolvedValue(review);

    render(Page);

    expect(await screen.findAllByRole("region", { name: "Request options" })).toHaveLength(2);
    await fireEvent.click(screen.getByRole("checkbox", { name: "Accept Description" }));
    await fireEvent.click(screen.getByRole("button", { name: "Deselect tag Drama" }));
    await fireEvent.click(screen.getByRole("checkbox", { name: "Select Amy Adams" }));
    await fireEvent.click(screen.getByRole("button", { name: "Select poster artwork from tmdb" }));
    await fireEvent.click(screen.getAllByRole("button", { name: "Request" })[0]);

    await waitFor(() => expect(mocks.commitReviewedRequest).toHaveBeenCalled());
    const [payload] = mocks.commitReviewedRequest.mock.calls[0] as [Record<string, unknown>];
    expect(payload.review).toEqual(review);
    expect(payload.selectedFields).not.toContain("description");
    expect(payload.selectedImages).toEqual({ poster: "https://images.test/two.jpg" });
    expect(payload.proposal).toEqual(expect.objectContaining({
      patch: expect.objectContaining({
        description: null,
        tags: ["Science Fiction"],
        credits: [],
      }),
      images: [expect.objectContaining({ url: "https://images.test/two.jpg" })],
      relationships: [],
    }));
  });

  it("drills into child proposal details without reloading provider data", async () => {
    const review = seriesReview();
    setRoute(
      REQUEST_MEDIA_KIND.series,
      review.externalIdentity.value,
      `plugin=${review.pluginId}&namespace=${review.externalIdentity.namespace}`,
    );
    mocks.reviewRequest.mockResolvedValue(review);

    render(Page);

    await screen.findByRole("heading", { name: "Andor" });
    await fireEvent.click(screen.getByRole("button", { name: "Review Season 1" }));

    expect(screen.getByRole("heading", { name: "Season 1" })).toBeInTheDocument();
    expect(screen.getByText("Episode 1")).toBeInTheDocument();
    expect(mocks.reviewRequest).toHaveBeenCalledTimes(1);

    await fireEvent.click(screen.getByRole("button", { name: "Back to Andor" }));
    expect(screen.getByRole("heading", { name: "Andor" })).toBeInTheDocument();
  });

  it("never refetches a shallow child while walking the held review", async () => {
    const rootReview = seriesReview();
    const season = rootReview.proposal.children[0] as EntityMetadataProposal;
    rootReview.proposal.children = [
      { ...season, children: [] },
      rootReview.proposal.children[1] as EntityMetadataProposal,
    ];
    setRoute(
      REQUEST_MEDIA_KIND.series,
      rootReview.externalIdentity.value,
      `plugin=${rootReview.pluginId}&namespace=${rootReview.externalIdentity.namespace}`,
    );
    mocks.reviewRequest.mockResolvedValue(rootReview);

    render(Page);

    await screen.findByRole("heading", { name: "Andor" });
    await fireEvent.click(screen.getByRole("button", { name: "Review Season 1" }));

    expect(screen.getByRole("heading", { name: "Season 1" })).toBeInTheDocument();
    expect(screen.queryByText("Episode 1")).not.toBeInTheDocument();
    expect(mocks.reviewRequest).toHaveBeenCalledTimes(1);

    await fireEvent.click(screen.getByRole("button", { name: "Back to Andor" }));
    await fireEvent.click(screen.getByRole("button", { name: "Review Season 1" }));

    expect(screen.getByRole("heading", { name: "Season 1" })).toBeInTheDocument();
    expect(mocks.reviewRequest).toHaveBeenCalledTimes(1);
  });

  it("lets nested children be selected all or none from the shared review", async () => {
    const review = seriesReview();
    setRoute(
      REQUEST_MEDIA_KIND.series,
      review.externalIdentity.value,
      `plugin=${review.pluginId}&namespace=${review.externalIdentity.namespace}`,
    );
    mocks.reviewRequest.mockResolvedValue(review);

    render(Page);

    await screen.findByRole("heading", { name: "Andor" });
    await fireEvent.click(screen.getByRole("button", { name: "Review Season 1" }));

    expect(screen.getByRole("checkbox", { name: "Deselect Episode 1" })).toBeChecked();
    await fireEvent.click(screen.getByRole("button", { name: "Deselect all Episodes" }));
    expect(screen.getByRole("checkbox", { name: "Select Episode 1" })).not.toBeChecked();
    await fireEvent.click(screen.getByRole("button", { name: "Select all Episodes" }));
    expect(screen.getByRole("checkbox", { name: "Deselect Episode 1" })).toBeChecked();
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

    await screen.findAllByText("TV Default");
    await fireEvent.keyDown(screen.getAllByRole("button", { name: "Monitor" })[0], { key: "ArrowDown" });
    await fireEvent.pointerUp(
      within(screen.getByRole("listbox")).getByRole("option", { name: /future only/i }),
    );
    const requestButton = (await screen.findAllByRole("button", { name: "Request" }))[0];
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

    await screen.findAllByText("TV Default");
    await fireEvent.click(screen.getByRole("checkbox", { name: "Deselect Season 1" }));
    await fireEvent.click(screen.getByRole("checkbox", { name: "Deselect Season 2" }));

    for (const button of screen.getAllByRole("button", { name: "Request" })) {
      expect(button).toBeDisabled();
    }
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

    await screen.findAllByText("Movie Default");
    expect(screen.getByRole("checkbox", { name: "Accept Title" })).toBeChecked();
    expect(screen.queryByRole("checkbox", { name: /Official Trailer/ })).not.toBeInTheDocument();
    await fireEvent.click(screen.getAllByRole("button", { name: "Request" })[0]);

    await waitFor(() => {
      expect(mocks.commitReviewedRequest).toHaveBeenCalledWith(expect.objectContaining({
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
        review,
        proposal: expect.objectContaining({ proposalId: "movie-root" }),
        selectedFields: expect.arrayContaining(["title", "description"]),
        selectedImages: {},
      }), true);
    });
    expect(mocks.goto).toHaveBeenCalledWith("/movies/movie-entity");
  });

  it("commits audiobook intent with Book targets and lands on the Book entity", async () => {
    const review = audiobookReview();
    setRoute(
      REQUEST_MEDIA_KIND.audiobook,
      review.externalIdentity.value,
      `plugin=${review.pluginId}&namespace=${review.externalIdentity.namespace}`,
    );
    mocks.reviewRequest.mockResolvedValue(review);
    mocks.fetchAccessibleLibraryRoots.mockResolvedValue([bookRoot()]);
    mocks.fetchAcquisitionProfiles.mockResolvedValue([bookProfile()]);
    mocks.commitReviewedRequest.mockResolvedValue({
      containerEntityId: null,
      items: [{
        externalId: review.externalIdentity.value,
        title: "Project Hail Mary",
        outcome: REQUEST_COMMIT_OUTCOME.requested,
        entityId: "book-entity",
        acquisitionId: "acquisition-audiobook",
      }],
    });

    render(Page);

    await screen.findAllByText("Book Default");
    expect(mocks.reviewRequest).toHaveBeenCalledWith({
      kind: REQUEST_MEDIA_KIND.audiobook,
      pluginId: "open-library",
      externalIdentity: review.externalIdentity,
      hideNsfw: true,
    });
    await fireEvent.click(screen.getAllByRole("button", { name: "Request" })[0]);

    await waitFor(() => {
      expect(mocks.commitReviewedRequest).toHaveBeenCalledWith(expect.objectContaining({
        kind: REQUEST_MEDIA_KIND.audiobook,
        pluginId: "open-library",
        rootExternalIdentity: review.externalIdentity,
        proposalRevision: "audiobook-revision",
        selectedProposalIds: ["audiobook-root"],
        targetLibraryRootId: "root-books",
        profileId: "profile-book",
        review,
        proposal: expect.objectContaining({ proposalId: "audiobook-root" }),
      }), true);
    });
    expect(mocks.goto).toHaveBeenCalledWith("/books/book-entity");
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
    mocks.commitReviewedRequest.mockRejectedValue(
      new ApiError("Proposal changed", 409, PROBLEM_CODE.requestProposalChanged),
    );

    render(Page);

    await screen.findAllByText("Movie Default");
    await fireEvent.click(screen.getAllByRole("button", { name: "Request" })[0]);

    expect((await screen.findAllByText(/proposal changed after you reviewed it/i)).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("button", { name: "Reload review" })).toHaveLength(2);
    expect(mocks.goto).not.toHaveBeenCalled();
  });

  it("surfaces a non-revision request conflict without falsely claiming the proposal changed", async () => {
    const review = movieReview();
    setRoute(
      REQUEST_MEDIA_KIND.movie,
      review.externalIdentity.value,
      `plugin=${review.pluginId}&namespace=${review.externalIdentity.namespace}`,
    );
    mocks.reviewRequest.mockResolvedValue(review);
    mocks.commitReviewedRequest.mockRejectedValue(new ApiError("This rendition is already requested.", 409));

    render(Page);

    await screen.findAllByText("Movie Default");
    await fireEvent.click(screen.getAllByRole("button", { name: "Request" })[0]);

    expect((await screen.findAllByText("This rendition is already requested.")).length).toBeGreaterThan(0);
    expect(screen.queryAllByText(/proposal changed after you reviewed it/i)).toHaveLength(0);
    expect(screen.queryAllByRole("button", { name: "Reload review" })).toHaveLength(0);
  });

  it("requires the plugin and namespace query contract", async () => {
    setRoute(REQUEST_MEDIA_KIND.movie, "Movie:Part:Case", "");

    render(Page);

    expect(await screen.findByText(/missing its plugin identity/i)).toBeInTheDocument();
    expect(mocks.reviewRequest).not.toHaveBeenCalled();
  });
});

function setRoute(kind: string, value: string, query: string) {
  page.params = { kind, id: value };
  page.url = new URL(
    `http://localhost/request/${kind}/${encodeURIComponent(value)}${query ? `?${query}` : ""}`,
  ) as unknown as typeof page.url;
}

function seriesReview(): RequestReviewResponse {
  const episode = proposal("episode-1", ENTITY_KIND.videoEpisode, "Episode 1", [], {
    seasonNumber: 1,
    episodeNumber: 1,
  });
  const seasonOne = proposal("season-1", ENTITY_KIND.videoSeason, "Season 1", [episode], { seasonNumber: 1 });
  const seasonTwo = proposal("season-2", ENTITY_KIND.videoSeason, "Season 2", [], { seasonNumber: 2 });
  const root = proposal("series-root", ENTITY_KIND.videoSeries, "Andor", [seasonOne, seasonTwo]);
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
        entityKind: ENTITY_KIND.videoEpisode,
        externalIdentity: { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Show:AbC:01:1:1" },
        requestable: true,
        position: 1,
      },
    ],
  };
}

function movieReview(): RequestReviewResponse {
  const trailer = proposal("trailer-metadata", ENTITY_KIND.video, "Official Trailer");
  const root = proposal("movie-root", ENTITY_KIND.movie, "Arrival", [trailer]);
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

function audiobookReview(): RequestReviewResponse {
  const root = proposal("audiobook-root", ENTITY_KIND.book, "Project Hail Mary");
  const externalIdentity = { namespace: "openlibrary", value: "works/OL:Project:Hail:Mary" };
  return {
    pluginId: "open-library",
    externalIdentity,
    entityKind: ENTITY_KIND.book,
    kind: REQUEST_MEDIA_KIND.audiobook,
    proposal: root,
    revision: "audiobook-revision",
    targets: [{
      proposalId: root.proposalId,
      kind: REQUEST_MEDIA_KIND.audiobook,
      entityKind: ENTITY_KIND.book,
      externalIdentity,
      requestable: true,
    }],
  };
}

function bookSiblingReview(): RequestReviewResponse {
  const volumeOne = proposal("book-volume-1", ENTITY_KIND.book, "Volume 1");
  const volumeTwo = proposal("book-volume-2", ENTITY_KIND.book, "Volume 2");
  const root = proposal("book-series-root", ENTITY_KIND.book, "A Book Series", [volumeOne, volumeTwo]);
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

function videoRoot(): LibraryRootSummary {
  return {
    id: "root-video",
    label: "Video Library",
    scanVideos: true,
    scanImages: false,
    scanAudio: false,
    scanBooks: false,
    isNsfw: false,
  };
}

function bookRoot(): LibraryRootSummary {
  return {
    id: "root-books",
    label: "Book Library",
    scanVideos: false,
    scanImages: false,
    scanAudio: false,
    scanBooks: true,
    isNsfw: false,
  };
}

function bookProfile(): BookAcquisitionProfileView {
  return {
    id: "profile-book",
    kind: ENTITY_KIND.book,
    displayName: "Book Default",
    isDefault: true,
    targetLibraryRootId: "root-books",
  } as BookAcquisitionProfileView;
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
