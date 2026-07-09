import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
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
      entityKind: "video-season",
      onActiveFilterIdsChange: onChange,
    });

    expect(screen.getByText("Availability")).toBeInTheDocument();
    expect(screen.queryByText("Has file")).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Downloaded" }));
    expect(onChange).toHaveBeenCalledWith(["availability:downloaded"]);
  });
});
