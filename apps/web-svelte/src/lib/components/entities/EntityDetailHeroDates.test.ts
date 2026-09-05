import { fireEvent, render, screen, waitFor, within } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import { ENTITY_DATE_TYPE as DATE } from "$lib/entities/entity-codes";
import type { EntityDetailDate } from "$lib/entities/entity-detail";
import EntityDetailHeroDates from "./EntityDetailHeroDates.svelte";

const dates: EntityDetailDate[] = [
  { code: DATE.digitalRelease, label: "Digital release", value: "2026-08-11", display: "Aug 11, 2026", sortable: null },
  { code: DATE.theatricalRelease, label: "Theatrical release", value: "2026-07-01", display: "Jul 1, 2026", sortable: null },
  { code: DATE.physicalRelease, label: "Physical release", value: "2026-09-08", display: "Sep 8, 2026", sortable: null },
];

describe("EntityDetailHeroDates", () => {
  it("shows a concise summary and opens every original date in a labeled popover", async () => {
    render(EntityDetailHeroDates, { dates });
    expect(screen.getByText("Jul 1, 2026")).toBeInTheDocument();
    expect(screen.queryByText("Aug 11, 2026")).not.toBeInTheDocument();
    const trigger = screen.getByRole("button", { name: "Show all 3 dates" });
    trigger.focus();
    await fireEvent.click(trigger);
    const dialog = await screen.findByRole("dialog", { name: "Dates" });
    for (const date of dates) {
      expect(within(dialog).getByText(date.label)).toBeInTheDocument();
      expect(within(dialog).getByText(date.display)).toBeInTheDocument();
    }
    await fireEvent.click(within(dialog).getByRole("button", { name: "Close dates" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await waitFor(() => expect(trigger).toHaveFocus());
    await fireEvent.click(trigger);
    const reopened = await screen.findByRole("dialog", { name: "Dates" });
    await fireEvent.keyDown(within(reopened).getByRole("button", { name: "Close dates" }), { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await waitFor(() => expect(trigger).toHaveFocus());
  });
  it("does not create empty separators or controls with no dates", () => {
    const { container } = render(EntityDetailHeroDates, { dates: [], leadingSeparator: true });
    expect(container.querySelector(".meta-sep")).toBeNull();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
  it("keeps a single date inline without an unnecessary disclosure", () => {
    render(EntityDetailHeroDates, { dates: [dates[0]!] });
    expect(screen.getByText("Digital release")).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
});
