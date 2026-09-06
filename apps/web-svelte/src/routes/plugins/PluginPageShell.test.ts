import { fireEvent, render, screen } from "@testing-library/svelte";
import { createRawSnippet } from "svelte";
import { describe, expect, it, vi } from "vitest";
import PluginPageShell from "./PluginPageShell.svelte";

describe("Plugin navigation", () => {
  it("uses named tabs and routes activation through the existing page callback", async () => {
    const onTabChange = vi.fn();
    render(PluginPageShell, {
      loading: false, error: null, message: null, tab: "installed",
      visibleTabs: [
        { key: "installed", label: "Installed", count: 6, nsfw: false },
        { key: "prismedia-index", label: "Community", count: 4, nsfw: false },
      ], onTabChange, onDismissError: vi.fn(),
      children: createRawSnippet(() => ({ render: () => "<p>Plugin content</p>" })),
    });
    expect(screen.getByRole("tab", { name: "Installed 6" }).getAttribute("aria-selected")).toBe("true");
    expect(screen.getByRole("tabpanel")).toBeTruthy();
    await fireEvent.click(screen.getByRole("tab", { name: "Community 4" }));
    expect(onTabChange).toHaveBeenCalledWith("prismedia-index");
  });
});
