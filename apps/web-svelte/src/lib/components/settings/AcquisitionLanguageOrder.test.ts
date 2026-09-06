import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, expect, it, vi } from "vitest";
import AcquisitionLanguageOrder from "./AcquisitionLanguageOrder.svelte";
afterEach(cleanup);
it("lets users reorder and remove preferred languages without editing codes", async () => {
  const onchange = vi.fn();
  render(AcquisitionLanguageOrder, { languages: ["English", "Japanese"], presets: [], onchange });
  await fireEvent.click(screen.getByRole("button", { name: "Move Japanese earlier" }));
  expect(onchange).toHaveBeenLastCalledWith(["Japanese", "English"]);
  await fireEvent.click(screen.getByRole("button", { name: "Remove English" }));
  expect(onchange).toHaveBeenLastCalledWith(["Japanese"]);
});
