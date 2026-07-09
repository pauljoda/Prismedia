import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import type { EntityMetadataProposal } from "$lib/api/identify-types";
import ProposalReviewSummary from "./ProposalReviewSummary.svelte";

describe("ProposalReviewSummary", () => {
  it("composes shared proposal metadata and selectable structural children", async () => {
    const onSelectedChange = vi.fn();
    const root = proposal();
    render(ProposalReviewSummary, {
      props: {
        proposal: root,
        selectedIds: [],
        selectableIds: ["season-1"],
        onSelectedChange,
        childrenTitle: "Seasons",
      },
    });

    expect(screen.getByText("Metadata")).toBeInTheDocument();
    expect(screen.getByText("Seasons")).toBeInTheDocument();
    expect(screen.getByText("Related metadata")).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("checkbox", { name: "Select Season 1" }));
    expect(onSelectedChange).toHaveBeenCalledWith("season-1", true);
    expect(screen.getAllByRole("checkbox")).toHaveLength(1);
  });
});

function proposal(): EntityMetadataProposal {
  return {
    proposalId: "series-1",
    provider: "tmdb",
    targetKind: "video-series",
    confidence: 1,
    matchReason: "external-id",
    patch: {
      title: "Andor",
      description: "A rebellion begins.",
      externalIds: { tmdb: "83867" },
      urls: [],
      tags: ["Drama"],
      studio: null,
      credits: [],
      dates: {},
      stats: {},
      positions: {},
      classification: null,
    },
    images: [],
    children: [{
      proposalId: "season-1",
      provider: "tmdb",
      targetKind: "video-season",
      confidence: 1,
      matchReason: "structure",
      patch: {
        title: "Season 1",
        description: null,
        externalIds: { tmdbseason: "83867:1" },
        urls: [],
        tags: [],
        studio: null,
        credits: [],
        dates: {},
        stats: {},
        positions: { seasonNumber: 1 },
        classification: null,
      },
      images: [],
      children: [],
      relationships: [],
      candidates: [],
    }],
    relationships: [{
      proposalId: "person-1",
      provider: "tmdb",
      targetKind: "person",
      confidence: 1,
      matchReason: "cast",
      patch: {
        title: "Diego Luna",
        description: null,
        externalIds: { tmdb: "25072" },
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
    }],
    candidates: [],
  };
}
