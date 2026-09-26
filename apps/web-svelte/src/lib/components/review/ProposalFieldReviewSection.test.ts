import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import type { EntityMetadataProposal } from "$lib/api/identify-types";
import { CREDIT_ROLE, ENTITY_KIND, METADATA_PATCH_FIELD } from "$lib/api/generated/codes";
import ProposalFieldReviewSection from "./ProposalFieldReviewSection.svelte";

describe("ProposalFieldReviewSection", () => {
  it("shows selectable creator credits and studios without relationship proposals", async () => {
    const value = proposal();
    value.patch.credits = [{ name: "Manga Author", role: CREDIT_ROLE.creator, character: null, sortOrder: 0 }];
    value.patch.studio = "Publication House";
    const onFieldChange = vi.fn();
    render(ProposalFieldReviewSection, { proposal: value, selectedFields: { [METADATA_PATCH_FIELD.credits]: true }, onFieldChange });
    expect(screen.getByText("Manga Author")).toBeInTheDocument();
    expect(screen.getByText("Publication House")).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("checkbox", { name: "Accept Credits" }));
    expect(onFieldChange).toHaveBeenCalledWith(METADATA_PATCH_FIELD.credits, false);
  });

  it("keeps credits visible when only some creators have person proposals", () => {
    const value = proposal();
    value.patch.credits = ["Writer", "Artist"].map((name, sortOrder) => ({ name, role: CREDIT_ROLE.creator, character: null, sortOrder }));
    value.relationships = [{ ...proposal(), proposalId: "writer", targetKind: ENTITY_KIND.person, patch: { ...proposal().patch, title: "Writer" } }];
    render(ProposalFieldReviewSection, { proposal: value });
    expect(screen.getByText("Writer, Artist")).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Accept Credits" })).toBeInTheDocument();
  });

  it("leaves fully represented credits to their person cards", () => {
    const value = proposal();
    value.patch.credits = [{ name: "Manga Author", role: CREDIT_ROLE.creator, character: null, sortOrder: 0 }];
    value.relationships = [{ ...proposal(), proposalId: "author", targetKind: ENTITY_KIND.person, patch: { ...proposal().patch, title: "Manga Author" } }];
    render(ProposalFieldReviewSection, { proposal: value });
    expect(screen.queryByRole("checkbox", { name: "Accept Credits" })).not.toBeInTheDocument();
  });
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

  it("renders proposal metadata without fake current values or selection controls", () => {
    render(ProposalFieldReviewSection, {
      props: {
        proposal: proposal(),
        variant: "summary",
        selectable: false,
        title: "Metadata",
      },
    });

    expect(screen.getByText("Metadata")).toBeInTheDocument();
    expect(screen.getByText("3 fields")).toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(screen.queryByText("Current")).not.toBeInTheDocument();
    expect(screen.getByText("Proposed")).toBeInTheDocument();
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
