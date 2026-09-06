import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, expect, it, vi } from "vitest";
import ToggleCard from "./ToggleCard.svelte";

afterEach(cleanup);

it("uses a single named switch for both its label and control", async () => {
  const onChange = vi.fn();
  render(ToggleCard, { checked: false, label: "Watch library", description: "Watch for new files.", onChange });
  const control = screen.getByRole("switch", { name: "Watch library" });
  expect(control.parentElement?.closest("button")).toBeNull();
  await fireEvent.click(screen.getByText("Watch library"));
  expect(onChange).toHaveBeenCalledExactlyOnceWith(true);
});
