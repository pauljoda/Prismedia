import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import { AVAILABILITY_FILTER_DEFS } from "$lib/entities/entity-grid";
import EntityGridFilterDrawer from "./EntityGridFilterDrawer.svelte";

describe("EntityGridFilterDrawer availability", () => {
  it("shows one exclusive availability family", async () => {
    const onChange = vi.fn();
    render(EntityGridFilterDrawer, {
      activeFilterIds: ["availability:wanted"],
      filterOptions: AVAILABILITY_FILTER_DEFS.map((definition) => ({
        ...definition,
        count: 0,
      })),
      entityKind: ENTITY_KIND.videoSeason,
      onActiveFilterIdsChange: onChange,
    });

    expect(screen.getByText("Availability")).toBeInTheDocument();
    expect(screen.queryByText("Has file")).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Downloaded" }));
    expect(onChange).toHaveBeenCalledWith(["availability:downloaded"]);
  });

  it("derives status visibility and vocabulary from the entity definition", async () => {
    const { rerender } = render(EntityGridFilterDrawer, {
      activeFilterIds: [],
      filterOptions: [],
      entityKind: ENTITY_KIND.bookChapter,
      onActiveFilterIdsChange: vi.fn(),
    });

    expect(screen.getByRole("button", { name: "Read" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Unread" })).toBeInTheDocument();

    await rerender({
      activeFilterIds: [],
      filterOptions: [],
      entityKind: ENTITY_KIND.person,
      onActiveFilterIdsChange: vi.fn(),
    });

    expect(screen.queryByText("Status")).not.toBeInTheDocument();
  });
});
