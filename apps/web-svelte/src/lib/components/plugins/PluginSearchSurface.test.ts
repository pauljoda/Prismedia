import { readFileSync } from "node:fs";
import { render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import type { PluginProvider } from "$lib/api/identify-types";
import PluginSearchSurface from "./PluginSearchSurface.svelte";

describe("PluginSearchSurface adoption", () => {
  it("keeps Request and both Identify search states on the same schema-driven surface", () => {
    const sharedConsumers = [
      "src/lib/components/requests/RequestDiscover.svelte",
      "src/lib/components/identify/IdentifyReviewChoice.svelte",
    ];

    for (const path of sharedConsumers) {
      expect(readFileSync(path, "utf8")).toContain("PluginSearchSurface");
    }

    const identifyRoute = readFileSync("src/routes/identify/[entityId]/+page.svelte", "utf8");
    expect(identifyRoute).toContain("<IdentifyReviewChoice");
    expect(identifyRoute).not.toContain("identify-manual-query");
  });

  it("shows the provider warning without a contradictory candidate prompt", () => {
    render(PluginSearchSurface, {
      props: {
        providers: [],
        selectedProviderId: "",
        fields: [],
        values: {},
        onProviderChange: vi.fn(),
        onValuesChange: vi.fn(),
        onSubmit: vi.fn(),
        onClear: vi.fn(),
        candidates: [{ externalIds: {}, title: "Stale candidate" }],
        entityKind: "book",
        onActivate: vi.fn(),
        noProvidersMessage: "No enabled provider supports book.",
      },
    });

    expect(screen.getByText("No enabled provider supports book.")).toBeInTheDocument();
    expect(screen.queryByText("Stale candidate")).not.toBeInTheDocument();
    expect(screen.queryByText("Enter the provider-specific details above to find candidates.")).not.toBeInTheDocument();
  });

  it("disables rescan while a provider search is in flight", () => {
    render(PluginSearchSurface, {
      props: {
        providers: [provider()],
        selectedProviderId: "openlibrary",
        fields: [{ key: "title", label: "Title", type: "text", required: true }],
        values: { title: "Artemis" },
        onProviderChange: vi.fn(),
        onValuesChange: vi.fn(),
        onSubmit: vi.fn(),
        onClear: vi.fn(),
        onRescan: vi.fn(),
        searching: true,
      },
    });

    expect(screen.getByRole("button", { name: "Rescan" })).toBeDisabled();
    expect(screen.getByRole("textbox", { name: "Title" })).toHaveValue("Artemis");
  });
});

function provider(): PluginProvider {
  return {
    id: "openlibrary",
    name: "Open Library",
    version: "1.0.0",
    installed: true,
    enabled: true,
    isNsfw: false,
    supports: [{ entityKind: "book", actions: ["search"] }],
    auth: [],
    missingAuthKeys: [],
  };
}
