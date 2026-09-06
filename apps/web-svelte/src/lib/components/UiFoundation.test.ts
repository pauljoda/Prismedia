import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { Badge, Button, Panel, TextInput } from "@prismedia/ui-svelte";

describe("shared UI foundation", () => {
  it("uses the badge base for semantic status without changing accessible text", () => {
    render(Badge, { variant: "success", role: "status", "aria-label": "Complete" });
    expect(screen.getByRole("status", { name: "Complete" })).toHaveAttribute("data-slot", "badge");
  });
  it("uses the shared button base without changing action callbacks", async () => {
    const onclick = vi.fn();
    render(Button, { "aria-label": "Save changes", variant: "primary", onclick });
    const button = screen.getByRole("button", { name: "Save changes" });
    expect(button).toHaveAttribute("data-slot", "button");
    expect(button).toHaveAttribute("type", "button");
    await fireEvent.click(button);
    expect(onclick).toHaveBeenCalledOnce();
  });

  it("carries validation into the input's accessible state", () => {
    render(TextInput, { "aria-label": "Title", value: "", variant: "error", required: true });
    const input = screen.getByRole("textbox", { name: "Title" });
    expect(input).toHaveAttribute("data-slot", "input");
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toBeRequired();
  });

  it("retains panel attributes on a card base", () => {
    render(Panel, { "aria-label": "Playback preferences", role: "region" });
    expect(screen.getByRole("region", { name: "Playback preferences" })).toHaveAttribute("data-slot", "card");
  });
});
