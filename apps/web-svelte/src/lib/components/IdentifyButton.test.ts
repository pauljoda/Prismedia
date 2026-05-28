import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import IdentifyButtonHarness from "./IdentifyButton.test-harness.svelte";
import type { IdentifyQueueItem, PluginProvider } from "$lib/api/identify-types";

const goto = vi.fn();
const fetchIdentifyProviders = vi.fn();
const fetchOptionalIdentifyQueueItem = vi.fn();

vi.mock("$app/navigation", () => ({
  goto: (...args: unknown[]) => goto(...args),
}));

vi.mock("$lib/api/identify-client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("$lib/api/identify-client")>();
  return {
    ...actual,
    fetchIdentifyProviders: (...args: unknown[]) => fetchIdentifyProviders(...args),
    fetchOptionalIdentifyQueueItem: (...args: unknown[]) => fetchOptionalIdentifyQueueItem(...args),
  };
});

describe("IdentifyButton", () => {
  beforeEach(() => {
    goto.mockReset();
    fetchIdentifyProviders.mockReset();
    fetchOptionalIdentifyQueueItem.mockReset();
    fetchIdentifyProviders.mockResolvedValue([provider("person")]);
    fetchOptionalIdentifyQueueItem.mockResolvedValue(null);
  });

  it("opens an existing queue review instead of starting a new identify flow", async () => {
    fetchOptionalIdentifyQueueItem.mockResolvedValue(queueItem("person-1", { state: "proposal" }));

    render(IdentifyButtonHarness, {
      props: {
        entityId: "person-1",
        entityKind: "person",
      },
    });

    const button = await screen.findByRole("button", { name: "Pending Review" });
    await fireEvent.click(button);

    expect(fetchIdentifyProviders).toHaveBeenCalledWith("person");
    expect(goto).toHaveBeenCalledWith("/identify/person-1?returnId=person-1&queued=1");
  });

  it("disables identify when no ready provider supports the entity kind", async () => {
    fetchIdentifyProviders.mockResolvedValue([provider("video")]);

    render(IdentifyButtonHarness, {
      props: {
        entityId: "person-1",
        entityKind: "person",
      },
    });

    const button = await screen.findByRole("button", { name: "No Provider" });
    expect(button).toBeDisabled();
  });

  it("keeps the identify label accessible while rendering icon-only on mobile", async () => {
    render(IdentifyButtonHarness, {
      props: {
        entityId: "person-1",
        entityKind: "person",
      },
    });

    const button = await screen.findByRole("button", { name: "Identify" });
    const label = await screen.findByText("Identify");

    expect(button.className).toContain("entity-action-button");
    expect(label.className).toContain("entity-action-button-label");
  });

  it("allows person identify when a registered provider supports people", async () => {
    render(IdentifyButtonHarness, {
      props: {
        entityId: "person-1",
        entityKind: "person",
      },
    });

    const button = await screen.findByRole("button", { name: "Identify" });
    await waitFor(() => expect(button).not.toBeDisabled());
    await fireEvent.click(button);

    expect(goto).toHaveBeenCalledWith("/identify/person-1?returnId=person-1");
  });
});

function provider(entityKind: string, options: Partial<PluginProvider> = {}): PluginProvider {
  return {
    id: options.id ?? "tmdb",
    name: options.name ?? "The Movie Database",
    version: options.version ?? "1.0.0",
    installed: options.installed ?? true,
    enabled: options.enabled ?? true,
    isNsfw: options.isNsfw ?? false,
    supports: options.supports ?? [{ entityKind, actions: ["search"] }],
    auth: options.auth ?? [],
    missingAuthKeys: options.missingAuthKeys ?? [],
  };
}

function queueItem(id: string, options: Partial<IdentifyQueueItem> = {}): IdentifyQueueItem {
  return {
    id: `queue-${id}`,
    entityId: id,
    entityKind: options.entityKind ?? "person",
    title: options.title ?? "Queued Person",
    isNsfw: options.isNsfw ?? false,
    state: options.state ?? "search",
    provider: options.provider ?? "tmdb",
    action: options.action ?? "search",
    query: options.query ?? null,
    candidates: options.candidates ?? [],
    proposal: options.proposal ?? null,
    error: options.error ?? null,
    createdAt: options.createdAt ?? "2026-05-25T00:00:00Z",
    updatedAt: options.updatedAt ?? "2026-05-25T00:00:00Z",
    completedAt: options.completedAt ?? null,
  };
}
