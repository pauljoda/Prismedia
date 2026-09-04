import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { BookOpen } from "@lucide/svelte";
import { ChoiceGroup } from "@prismedia/ui-svelte";

const options = [
  { value: "a", label: "Alpha", icon: BookOpen, count: 0 },
  { value: "b", label: "Bravo", disabled: true },
  { value: "c", label: "Charlie", count: 12 },
];

describe("ChoiceGroup", () => {
  it("retains a required single choice and does not repeat its action", async () => {
    const onValueChange = vi.fn();
    render(ChoiceGroup, { type: "single", options, value: "a", onValueChange, ariaLabel: "Content kinds" });
    const alpha = screen.getByRole("radio", { name: "Alpha 0" });
    await fireEvent.click(alpha);
    expect(alpha).toHaveAttribute("aria-checked", "true");
    expect(alpha.querySelector("svg")).toHaveAttribute("aria-hidden", "true");
    expect(onValueChange).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("radio", { name: "Charlie 12" }));
    expect(onValueChange).toHaveBeenCalledExactlyOnceWith("c");
    // The parent owns the value, including when it rejects a proposed change.
    expect(alpha).toHaveAttribute("aria-checked", "true");
  });

  it("supports multiple choices without deselecting the final active choice", async () => {
    const onValueChange = vi.fn();
    const { rerender } = render(ChoiceGroup, { type: "multiple", options, value: ["a", "c"], onValueChange, ariaLabel: "Search kinds" });
    await fireEvent.click(screen.getByRole("button", { name: "Alpha 0" }));
    expect(onValueChange).toHaveBeenLastCalledWith(["c"]);
    await rerender({ type: "multiple", options, value: ["c"], onValueChange, ariaLabel: "Search kinds" });
    onValueChange.mockClear();
    const charlie = screen.getByRole("button", { name: "Charlie 12" });
    await fireEvent.click(charlie);
    expect(charlie).toHaveAttribute("aria-pressed", "true");
    expect(onValueChange).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "Alpha 0" }));
    expect(onValueChange).toHaveBeenLastCalledWith(["a", "c"]);
  });

  it("honors disabled options and whole-group disabling", async () => {
    const onValueChange = vi.fn();
    const { rerender } = render(ChoiceGroup, { type: "single", options, value: "a", onValueChange, ariaLabel: "Kinds" });
    expect(screen.getByRole("radio", { name: "Bravo" })).toBeDisabled();
    await rerender({ type: "single", options, value: "a", onValueChange, ariaLabel: "Kinds", disabled: true });
    for (const choice of screen.getAllByRole("radio")) expect(choice).toBeDisabled();
    expect(onValueChange).not.toHaveBeenCalled();
  });
});
