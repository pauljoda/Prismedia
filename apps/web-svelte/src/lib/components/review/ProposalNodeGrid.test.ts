import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import type { EntityMetadataProposal } from "$lib/api/identify-types";
import ProposalNodeGrid from "./ProposalNodeGrid.svelte";

describe("ProposalNodeGrid", () => {
  it("selects and opens proposal nodes without adapting them into Entities", async () => {
    const node = proposal();
    const onSelectedChange = vi.fn();
    const onActivate = vi.fn();
    render(ProposalNodeGrid, {
      props: {
        nodes: [node],
        selectedIds: [],
        selectableIds: [node.proposalId],
        onSelectedChange,
        onActivate,
      },
    });

    await fireEvent.click(screen.getByRole("checkbox", { name: "Select Season 1" }));
    expect(onSelectedChange).toHaveBeenCalledWith("season-1", true);
    await fireEvent.click(screen.getByRole("button", { name: "Review Season 1" }));
    expect(onActivate).toHaveBeenCalledWith(node);
    expect(screen.getByText("S01")).toBeInTheDocument();
  });

  it("renders inert proposal context without fake actions or disabled selection controls", () => {
    render(ProposalNodeGrid, {
      props: {
        nodes: [proposal()],
        selectedIds: [],
        selectableIds: [],
        onSelectedChange: vi.fn(),
        selectionMode: false,
      },
    });

    expect(screen.queryByRole("button", { name: "Review Season 1" })).not.toBeInTheDocument();
    expect(screen.getByText("Season 1")).toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(screen.queryByText("Unavailable")).not.toBeInTheDocument();
  });
});

function proposal(): EntityMetadataProposal {
  return {
    proposalId: "season-1",
    provider: "metadata",
    targetKind: "video-season",
    confidence: 1,
    matchReason: null,
    patch: {
      title: "Season 1",
      description: null,
      externalIds: { tmdb: "series:1" },
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
  };
}
