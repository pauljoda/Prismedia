import { fireEvent } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { createNavigationKeyHandler } from "./navigation-keyboard";

describe("reader navigation keyboard ownership", () => {
  it("leaves consumed arrows to the active picker", () => {
    const next = vi.fn();
    const handler = createNavigationKeyHandler({ close: vi.fn(), prev: vi.fn(), next });
    const event = new KeyboardEvent("keydown", { key: "ArrowRight", cancelable: true });
    event.preventDefault();
    handler(event);
    expect(next).not.toHaveBeenCalled();
  });

  it("does not turn slider, radio, or text-field arrows into page navigation", async () => {
    const next = vi.fn();
    const handler = createNavigationKeyHandler({ close: vi.fn(), prev: vi.fn(), next });
    for (const role of ["slider", "radio", "combobox"]) {
      const control = document.createElement("button");
      control.setAttribute("role", role);
      control.addEventListener("keydown", handler);
      await fireEvent.keyDown(control, { key: "ArrowRight" });
    }
    expect(next).not.toHaveBeenCalled();
  });
});
