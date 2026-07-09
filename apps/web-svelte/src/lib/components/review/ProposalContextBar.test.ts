import { render, screen } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import type { EntityMetadataProposal } from "$lib/api/identify-types";
import ProposalContextBar from "./ProposalContextBar.svelte";

describe("ProposalContextBar", () => {
  it("renders proposal context without requiring a persisted Entity", () => {
    const { container } = render(ProposalContextBar, {
      props: {
        proposal: proposal(),
        title: "Andor",
        subtitle: "Current title",
        posterUrl: "https://images.example/andor.jpg",
        imageShape: "portrait",
        showReason: true,
      },
    });

    expect(screen.getByRole("heading", { name: "Andor" })).toBeInTheDocument();
    expect(screen.getByText("Current title")).toBeInTheDocument();
    expect(screen.getByText("92%")).toBeInTheDocument();
    expect(screen.getByText("cinema-metadata")).toBeInTheDocument();
    expect(screen.getByText("title-and-year")).toBeInTheDocument();
    expect(container.querySelector("img")).toHaveAttribute("referrerpolicy", "no-referrer");
  });
});

function proposal(): EntityMetadataProposal {
  return {
    proposalId: "proposal-1",
    provider: "cinema-metadata",
    targetKind: "video-series",
    confidence: 0.92,
    matchReason: "title-and-year",
    patch: {
      title: "Andor",
      description: null,
      externalIds: { tmdb: "83867" },
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
  };
}
