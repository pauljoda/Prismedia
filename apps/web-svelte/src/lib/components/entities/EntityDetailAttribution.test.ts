import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import { CAPABILITY_KIND, ENTITY_KIND } from "$lib/api/generated/codes";
import { entityCardToDetailCard } from "$lib/entities/entity-detail";
import EntityDetail from "./EntityDetail.test-harness.svelte";

describe("retained acquisition attribution in library details", () => {
  it("shows saved source statements without requiring a filesystem source capability", async () => {
    const card = entityCardToDetailCard({
      id: "image", kind: ENTITY_KIND.image, title: "Image", parentEntityId: null, sortOrder: null, childrenByKind: [], relationships: [],
      capabilities: [{ kind: CAPABILITY_KIND.acquisitionAttribution, unavailable: false, items: [{
        operationId: "operation", acceptedAt: "2026-09-17T00:00:00Z",
        attribution: { sourceUrl: "https://catalog.test/source", creator: "Original creator", licenseName: "Public domain",
          credit: null, licenseUrl: null, usageTerms: null, attributionRequired: false },
      }] }],
    });
    render(EntityDetail, { card });
    await fireEvent.click(screen.getByRole("button", { name: "Source attribution" }));
    expect(screen.getByText("Original creator")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Source page" })).toHaveAttribute("href", "https://catalog.test/source");
  });

  it("reports unreadable saved attribution while keeping the entity details usable", () => {
    const card = entityCardToDetailCard({
      id: "image", kind: ENTITY_KIND.image, title: "Image", parentEntityId: null, sortOrder: null, childrenByKind: [], relationships: [],
      capabilities: [{ kind: CAPABILITY_KIND.acquisitionAttribution, unavailable: true, items: [] }],
    });
    render(EntityDetail, { card });
    expect(screen.getByText("Some saved source attribution is unavailable.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Image" })).toBeInTheDocument();
  });
});
