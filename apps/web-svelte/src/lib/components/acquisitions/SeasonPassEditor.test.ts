import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  ENTITY_KIND,
  EXTERNAL_ID_PROVIDER,
  MONITOR_PRESET,
  MONITOR_STATUS,
  PROPOSAL_KIND,
  REQUEST_MEDIA_KIND,
} from "$lib/api/generated/codes";
import type { EntityMetadataProposal, RequestReviewResponse } from "$lib/api/generated/model";
import { ApiError } from "$lib/api/orval-fetch";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import SeasonPassEditor from "./SeasonPassEditor.svelte";

const mocks = vi.hoisted(() => ({
  commitEntityRequest: vi.fn(),
  commitReviewedRequest: vi.fn(),
  fetchMonitors: vi.fn(),
  stopMonitor: vi.fn(),
}));

vi.mock("$lib/api/requests", () => ({
  commitEntityRequest: mocks.commitEntityRequest,
  commitReviewedRequest: mocks.commitReviewedRequest,
}));

vi.mock("$lib/api/monitors", () => ({
  fetchMonitors: mocks.fetchMonitors,
  stopMonitor: mocks.stopMonitor,
}));

describe("SeasonPassEditor", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchMonitors.mockResolvedValue([{
      id: "series-monitor",
      kind: ENTITY_KIND.videoSeries,
      acquisitionId: null,
      status: MONITOR_STATUS.active,
      title: "Andor",
      author: null,
      acquisitionStatus: null,
      createdAt: "2026-07-09T00:00:00Z",
      updatedAt: "2026-07-09T00:00:00Z",
      entityId: "series-entity",
      preset: MONITOR_PRESET.missing,
    }]);
    mocks.commitReviewedRequest.mockResolvedValue({ containerEntityId: "series-entity", items: [] });
  });

  afterEach(cleanup);

  it("requests a provider-only season by reviewed proposal id and exact plugin revision", async () => {
    const onChanged = vi.fn(async () => {});
    render(SeasonPassEditor, {
      props: {
        seasonCards: [],
        seasonEpisodeCounts: {},
        providerReview: review(),
        seriesEntityId: "series-entity",
        onChanged,
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: /Season Pass/ }));
    const toggle = await screen.findByRole("switch", { name: "Monitor season 1" });
    await waitFor(() => expect(toggle).not.toBeDisabled());
    await fireEvent.click(toggle);

    await waitFor(() => {
      expect(mocks.commitReviewedRequest).toHaveBeenCalledWith(
        {
          kind: REQUEST_MEDIA_KIND.series,
          pluginId: "cinema-metadata",
          rootExternalIdentity: {
            namespace: EXTERNAL_ID_PROVIDER.tmdb,
            value: "Show:AbC:01",
          },
          proposalRevision: "series-revision",
          selectedProposalIds: ["season-1"],
          preset: MONITOR_PRESET.missing,
        },
        true,
      );
    });
    expect(mocks.commitEntityRequest).not.toHaveBeenCalled();
    expect(onChanged).toHaveBeenCalledOnce();
  });

  it("surfaces a stale reviewed proposal without leaving an optimistic toggle enabled", async () => {
    mocks.commitReviewedRequest.mockRejectedValue(new ApiError("Proposal changed", 409));
    render(SeasonPassEditor, {
      props: {
        seasonCards: [],
        seasonEpisodeCounts: {},
        providerReview: review(),
        seriesEntityId: "series-entity",
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: /Season Pass/ }));
    const toggle = await screen.findByRole("switch", { name: "Monitor season 1" });
    await waitFor(() => expect(toggle).not.toBeDisabled());
    await fireEvent.click(toggle);

    expect(await screen.findByText(/season list changed/i)).toBeInTheDocument();
    expect(toggle).toHaveAttribute("aria-checked", "false");
  });

  it("keeps existing local seasons on the Entity request path", async () => {
    mocks.commitEntityRequest.mockResolvedValue({ containerEntityId: null, items: [] });
    render(SeasonPassEditor, {
      props: {
        seasonCards: [localSeasonCard()],
        seasonEpisodeCounts: { "season-entity": 12 },
        providerReview: null,
        seriesEntityId: "series-entity",
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: /Season Pass/ }));
    const toggle = await screen.findByRole("switch", { name: "Monitor season 1" });
    await waitFor(() => expect(toggle).not.toBeDisabled());
    await fireEvent.click(toggle);

    await waitFor(() => expect(mocks.commitEntityRequest).toHaveBeenCalledWith("season-entity"));
    expect(mocks.commitReviewedRequest).not.toHaveBeenCalled();
  });
});

function localSeasonCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "season-entity",
      kind: ENTITY_KIND.videoSeason,
      title: "Season 1",
      parentEntityId: "series-entity",
      sortOrder: 1,
      capabilities: [],
      childrenByKind: [],
      relationships: [],
    },
    aspectRatio: "video",
    cover: null,
    hover: { kind: "none" },
  };
}

function review(): RequestReviewResponse {
  const rootIdentity = { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "Show:AbC:01" };
  const season = proposal("season-1", PROPOSAL_KIND.videoSeason, "Season 1");
  const root = proposal("series-root", PROPOSAL_KIND.videoSeries, "Andor", [season]);
  return {
    pluginId: "cinema-metadata",
    externalIdentity: rootIdentity,
    entityKind: ENTITY_KIND.videoSeries,
    kind: REQUEST_MEDIA_KIND.series,
    proposal: root,
    revision: "series-revision",
    targets: [
      {
        proposalId: root.proposalId,
        kind: REQUEST_MEDIA_KIND.series,
        entityKind: ENTITY_KIND.videoSeries,
        externalIdentity: rootIdentity,
        requestable: true,
      },
      {
        proposalId: season.proposalId,
        kind: REQUEST_MEDIA_KIND.season,
        entityKind: ENTITY_KIND.videoSeason,
        externalIdentity: { namespace: "tmdbseason", value: "Show:AbC:01:1" },
        requestable: true,
        position: 1,
      },
    ],
  };
}

function proposal(
  proposalId: string,
  targetKind: EntityMetadataProposal["targetKind"],
  title: string,
  children: EntityMetadataProposal[] = [],
): EntityMetadataProposal {
  return {
    proposalId,
    provider: "cinema-metadata",
    targetKind,
    confidence: 1,
    matchReason: "external-id",
    patch: {
      title,
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
    children,
    relationships: [],
    candidates: [],
  };
}
