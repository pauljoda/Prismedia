import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, expect, it, vi } from "vitest";
import { Toggle } from "@prismedia/ui-svelte";

afterEach(cleanup);

it("keeps the parent as the source of truth until a change is accepted", async () => {
  const onchange = vi.fn();
  const { rerender } = render(Toggle, { checked: false, ariaLabel: "Watch files", onchange });
  const control = screen.getByRole("switch", { name: "Watch files" });
  await fireEvent.click(control);
  expect(onchange).toHaveBeenCalledExactlyOnceWith(true);
  expect(control).not.toBeChecked();
  await rerender({ checked: true });
  expect(control).toBeChecked();
  await fireEvent.click(control);
  expect(onchange).toHaveBeenLastCalledWith(false);
  expect(control).toBeChecked();
});

it("does not request a change when disabled", async () => {
  const onchange = vi.fn();
  render(Toggle, { checked: false, ariaLabel: "Watch files", onchange, disabled: true });
  await fireEvent.click(screen.getByRole("switch", { name: "Watch files" }));
  expect(onchange).not.toHaveBeenCalled();
});
