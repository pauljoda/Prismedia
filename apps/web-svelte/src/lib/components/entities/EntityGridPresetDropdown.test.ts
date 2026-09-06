import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import EntityGridPresetDropdown from "./EntityGridPresetDropdown.svelte";
import type { FilterPreset } from "$lib/filter-presets";
import { ENTITY_LIST_SORT, ENTITY_SORT_DIRECTION } from "$lib/api/generated/codes";

const preset: FilterPreset = { id: "saved", name: "Unread", filters: [], sortBy: ENTITY_LIST_SORT.title, sortDir: ENTITY_SORT_DIRECTION.ascending };

describe("EntityGridPresetDropdown", () => {
  it("dismisses with Escape, restores focus and resets unfinished naming", async () => {
    render(EntityGridPresetDropdown);
    const trigger = screen.getByRole("button", { name: "Presets" });
    trigger.focus();
    await fireEvent.click(trigger);
    expect(await screen.findByRole("dialog", { name: "Filter presets" })).toHaveAttribute("id", trigger.getAttribute("aria-controls"));
    await fireEvent.click(screen.getByRole("button", { name: "Save current filters" }));
    const input = screen.getByRole("textbox");
    await fireEvent.input(input, { target: { value: "Unfinished" } });
    await fireEvent.keyDown(input, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("button", { name: "Save current filters" })).not.toBeInTheDocument());
    await waitFor(() => expect(trigger).toHaveFocus());
    await fireEvent.click(trigger);
    await fireEvent.click(screen.getByRole("button", { name: "Save current filters" }));
    expect(screen.getByRole("textbox", { name: "Preset name" })).toHaveValue("");
  });

  it("saves trimmed names, rejects blank names and closes after saving", async () => {
    const onSavePreset = vi.fn();
    render(EntityGridPresetDropdown, { onSavePreset });
    await fireEvent.click(screen.getByRole("button", { name: "Presets" }));
    await fireEvent.click(screen.getByRole("button", { name: "Save current filters" }));
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
    const input = screen.getByRole("textbox", { name: "Preset name" });
    await waitFor(() => expect(input).toHaveFocus());
    await fireEvent.input(input, { target: { value: "  Unread books  " } });
    await fireEvent.submit(input.closest("form")!);
    expect(onSavePreset).toHaveBeenCalledWith("Unread books");
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("keeps delete separate from apply and explicitly confirms overwrites", async () => {
    const onApplyPreset = vi.fn();
    const onDeletePreset = vi.fn();
    const onOverwritePreset = vi.fn();
    render(EntityGridPresetDropdown, { presets: [preset], activePresetId: preset.id, onApplyPreset, onDeletePreset, onOverwritePreset });
    const trigger = screen.getByRole("button", { name: "Unread" });
    await fireEvent.click(trigger);
    await fireEvent.click(screen.getByRole("button", { name: "Delete preset Unread" }));
    expect(onDeletePreset).toHaveBeenCalledWith(preset.id);
    expect(onApplyPreset).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "Save current filters" }));
    expect(onOverwritePreset).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "Overwrite" }));
    expect(onOverwritePreset).toHaveBeenCalledWith(preset.id);
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await fireEvent.click(trigger);
    await fireEvent.click(screen.getByRole("button", { name: "Apply preset Unread" }));
    expect(onApplyPreset).toHaveBeenCalledWith(preset);
  });
});
