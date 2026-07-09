import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import type { EntityMetadataProposal } from "$lib/api/identify-types";
import ProposalFieldReviewSection from "./ProposalFieldReviewSection.svelte";

describe("ProposalFieldReviewSection", () => {
  it("reviews the same proposal with or without current Entity values", async () => {
    const onFieldChange = vi.fn();
    const onAllFields = vi.fn();
    render(ProposalFieldReviewSection, {
      props: {
        proposal: proposal(),
        selectedFields: { title: true, description: false },
        currentValue: (field) => field === "title" ? "Old title" : "",
        onFieldChange,
        onAllFields,
      },
    });

    expect(screen.getByText("Old title")).toBeInTheDocument();
    expect(screen.getByText("New title")).toBeInTheDocument();
    expect(screen.getByText("A new description")).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("checkbox", { name: "Accept Description" }));
    expect(onFieldChange).toHaveBeenCalledWith("description", true);
    await fireEvent.click(screen.getByRole("button", { name: "None" }));
    expect(onAllFields).toHaveBeenCalledWith(false);
  });
});

function proposal(): EntityMetadataProposal {
  return {
    proposalId: "proposal-1",
    provider: "metadata",
    targetKind: "movie",
    confidence: 1,
    matchReason: null,
    patch: {
      title: "New title",
      description: "A new description",
      externalIds: { tmdb: "1" },
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
