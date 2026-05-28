import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IdentifyQueueItem, PluginProvider } from "$lib/api/identify-types";
import UseIdentifyDetailActionHarness from "./use-identify-detail-action.test-harness.svelte";

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

describe("useIdentifyDetailAction", () => {
  beforeEach(() => {
    goto.mockReset();
    fetchIdentifyProviders.mockReset();
    fetchOptionalIdentifyQueueItem.mockReset();
    fetchIdentifyProviders.mockResolvedValue([provider("person")]);
    fetchOptionalIdentifyQueueItem.mockResolvedValue(null);
  });

  it("opens an existing queue review instead of starting a new identify flow", async () => {
    fetchOptionalIdentifyQueueItem.mockResolvedValue(queueItem("person-1", { state: "proposal" }));

    render(UseIdentifyDetailActionHarness, {
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

  it("hides the detail action when no ready provider supports the entity kind", async () => {
    fetchIdentifyProviders.mockResolvedValue([provider("video")]);

    render(UseIdentifyDetailActionHarness, {
      props: {
        entityId: "person-1",
        entityKind: "person",
      },
    });

    await waitFor(() => expect(fetchIdentifyProviders).toHaveBeenCalledWith("person"));
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("shows a plain identify action when a provider supports the entity kind", async () => {
    render(UseIdentifyDetailActionHarness, {
      props: {
        entityId: "person-1",
        entityKind: "person",
      },
    });

    const button = await screen.findByRole("button", { name: "Identify" });
    await fireEvent.click(button);

    expect(goto).toHaveBeenCalledWith("/identify/person-1?returnId=person-1");
  });

  it("loads provider state after the detail entity is populated asynchronously", async () => {
    fetchIdentifyProviders.mockResolvedValue([provider("video")]);
    const { rerender } = render(UseIdentifyDetailActionHarness, {
      props: {
        entityId: "",
        entityKind: "",
      },
    });

    await rerender({
      entityId: "video-1",
      entityKind: "video",
    });

    await waitFor(() => expect(fetchIdentifyProviders).toHaveBeenCalledWith("video"));
    expect(await screen.findByRole("button", { name: "Identify" })).toBeInTheDocument();
  });

  it("refreshes provider state when an open detail page regains focus", async () => {
    fetchIdentifyProviders
      .mockResolvedValueOnce([])
      .mockResolvedValue([provider("video")]);

    render(UseIdentifyDetailActionHarness, {
      props: {
        entityId: "video-1",
        entityKind: "video",
      },
    });

    await waitFor(() => expect(fetchIdentifyProviders).toHaveBeenCalledWith("video"));
    expect(screen.queryByRole("button")).toBeNull();

    window.dispatchEvent(new Event("focus"));

    expect(await screen.findByRole("button", { name: "Identify" })).toBeInTheDocument();
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
