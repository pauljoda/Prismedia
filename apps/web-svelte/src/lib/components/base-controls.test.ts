import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { Checkbox, Meter, Progress } from "@prismedia/ui-svelte";
import PasswordField from "./forms/PasswordField.svelte";
describe("base controls", () => {
  it("keeps fractional progress ranges consistent between the visual fill and ARIA", () => {
    render(Progress, { value: 0.25, max: 0.5, "aria-label": "Fraction" });
    const progress = screen.getByRole("progressbar", { name: "Fraction" });
    expect(progress).toHaveAttribute("aria-valuemax", "0.5");
    expect(progress).toHaveAttribute("aria-valuenow", "0.25");
    expect(progress.querySelector("[data-slot=progress-indicator]")).toHaveStyle("transform: translateX(-50%)");
  });
  it("renders the mixed checkbox and reports a checked value without committing parent state", async () => {
    const onchange = vi.fn();
    render(Checkbox, { indeterminate: true, checked: false, onchange, "aria-label": "Select all" });
    const control = screen.getByRole("checkbox", { name: "Select all" });
    expect(control).toHaveAttribute("aria-checked", "mixed");
    await fireEvent.click(control);
    expect(onchange).toHaveBeenCalledExactlyOnceWith(true);
  });
  it("announces progress and clamps over-capacity values", () => {
    render(Meter, { value: 150, max: 100, label: "Cache" });
    expect(screen.getByRole("progressbar", { name: "Cache" })).toHaveAttribute("aria-valuenow", "100");
  });
  it("keeps password reveal keyboard accessible and disables it with the field", async () => {
    const view = render(PasswordField, { value: "secret", label: "Password", onChange: vi.fn() });
    const reveal = screen.getByRole("button", { name: "Show password" });
    expect(reveal.tabIndex).toBe(0);
    await fireEvent.click(reveal);
    expect(screen.getByLabelText("Password")).toHaveAttribute("type", "text");
    await view.rerender({ disabled: true });
    expect(screen.getByRole("button", { name: "Hide password" })).toBeDisabled();
  });
});
