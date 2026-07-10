import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CAPABILITY_KIND, ENTITY_FILE_ROLE, ENTITY_KIND } from "$lib/api/generated/codes";
import type { EntityCapability } from "$lib/api/generated/model";
import type { IdentifyQueueItem, PluginProvider } from "$lib/api/identify-types";
import UseIdentifyDetailActionHarness from "./use-identify-detail-action.test-harness.svelte";

const goto = vi.fn();
const fetchIdentifyProviders = vi.fn();
const fetchOptionalIdentifyQueueItem = vi.fn();
const requestIdentifySearch = vi.fn();

vi.mock("$app/navigation", () => ({
  goto: (...args: unknown[]) => goto(...args),
}));

vi.mock("$lib/api/identify-client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("$lib/api/identify-client")>();
  return {
    ...actual,
    fetchIdentifyProviders: (...args: unknown[]) => fetchIdentifyProviders(...args),
    fetchOptionalIdentifyQueueItem: (...args: unknown[]) => fetchOptionalIdentifyQueueItem(...args),
    requestIdentifySearch: (...args: unknown[]) => requestIdentifySearch(...args),
  };
});

describe("useIdentifyDetailAction", () => {
  beforeEach(() => {
    goto.mockReset();
    fetchIdentifyProviders.mockReset();
    fetchOptionalIdentifyQueueItem.mockReset();
    requestIdentifySearch.mockReset();
    fetchIdentifyProviders.mockResolvedValue([provider("person")]);
    fetchOptionalIdentifyQueueItem.mockResolvedValue(null);
    requestIdentifySearch.mockResolvedValue(queueItem("person-1", { state: "queued" }));
  });

  it("opens an existing queue review instead of starting a new identify flow", async () => {
    fetchOptionalIdentifyQueueItem.mockResolvedValue(queueItem("person-1", { state: "proposal" }));

    render(UseIdentifyDetailActionHarness, {
      props: {
        entityId: "person-1",
        entityKind: "person",
        capabilities: eligibleCapabilities(),
        hasSourceMedia: true,
      },
    });

    const button = await screen.findByRole("button", { name: "Pending Review" });
    await fireEvent.click(button);

    expect(fetchIdentifyProviders).toHaveBeenCalledWith("person");
    expect(requestIdentifySearch).not.toHaveBeenCalled();
    expect(goto).toHaveBeenCalledWith("/identify/person-1?returnId=person-1");
  });

  it("links to Plugins when no ready provider supports the entity kind", async () => {
    fetchIdentifyProviders.mockResolvedValue([provider("video")]);

    render(UseIdentifyDetailActionHarness, {
      props: {
        entityId: "person-1",
        entityKind: "person",
        capabilities: eligibleCapabilities(),
        hasSourceMedia: true,
      },
    });

    const button = await screen.findByRole("button", {
      name: "Identify (no compatible plugin installed)",
    });
    expect(button).not.toBeDisabled();
    await fireEvent.click(button);

    expect(goto).toHaveBeenCalledWith("/plugins");
  });

  it("shows a plain identify action when a provider supports the entity kind", async () => {
    render(UseIdentifyDetailActionHarness, {
      props: {
        entityId: "person-1",
        entityKind: "person",
        capabilities: eligibleCapabilities(),
        hasSourceMedia: true,
      },
    });

    const button = await screen.findByRole("button", { name: "Identify" });
    await fireEvent.click(button);

    await waitFor(() => {
      expect(goto).toHaveBeenCalledWith("/identify/person-1?returnId=person-1");
    });
    expect(requestIdentifySearch).toHaveBeenCalledWith("person-1", null);
  });

  it("loads provider state after the detail entity is populated asynchronously", async () => {
    fetchIdentifyProviders.mockResolvedValue([provider("video")]);
    const { rerender } = render(UseIdentifyDetailActionHarness, {
      props: {
        entityId: "",
        entityKind: ENTITY_KIND.video,
        capabilities: [],
        hasSourceMedia: false,
      },
    });

    await rerender({
      entityId: "video-1",
      entityKind: "video",
      capabilities: eligibleCapabilities(),
      hasSourceMedia: true,
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
        capabilities: eligibleCapabilities(),
        hasSourceMedia: true,
      },
    });

    await waitFor(() => expect(fetchIdentifyProviders).toHaveBeenCalledWith("video"));
    expect(
      await screen.findByRole("button", { name: "Identify (no compatible plugin installed)" }),
    ).toBeInTheDocument();

    window.dispatchEvent(new Event("focus"));

    expect(await screen.findByRole("button", { name: "Identify" })).toBeInTheDocument();
  });

  it("returns no action and makes no identify calls for an Entity without a direct Source file", async () => {
    render(UseIdentifyDetailActionHarness, {
      props: {
        entityId: "video-1",
        entityKind: "video",
        capabilities: [{
          kind: CAPABILITY_KIND.files,
          items: [{ role: ENTITY_FILE_ROLE.thumbnail, path: "/cache/thumb.webp", mimeType: "image/webp" }],
        }],
        hasSourceMedia: false,
      },
    });

    await waitFor(() => expect(screen.queryByRole("button")).not.toBeInTheDocument());
    expect(fetchOptionalIdentifyQueueItem).not.toHaveBeenCalled();
    expect(fetchIdentifyProviders).not.toHaveBeenCalled();
    expect(requestIdentifySearch).not.toHaveBeenCalled();
  });

  it("offers Identify for a non-Wanted wrapper with source-backed descendants", async () => {
    fetchIdentifyProviders.mockResolvedValue([provider("video-series")]);
    render(UseIdentifyDetailActionHarness, {
      props: {
        entityId: "series-1",
        entityKind: "video-series",
        capabilities: [{
          kind: CAPABILITY_KIND.fileManagement,
          canDeleteFiles: true,
        }],
        hasSourceMedia: true,
      },
    });

    expect(await screen.findByRole("button", { name: "Identify" })).toBeInTheDocument();
    expect(fetchIdentifyProviders).toHaveBeenCalledWith("video-series");
  });

  it("returns no action and makes no identify calls for a Wanted Entity", async () => {
    render(UseIdentifyDetailActionHarness, {
      props: {
        entityId: "wanted-video",
        entityKind: "video",
        capabilities: [
          ...eligibleCapabilities(),
          {
            kind: CAPABILITY_KIND.flags,
            isFavorite: false,
            isNsfw: false,
            isOrganized: false,
            isWanted: true,
          },
        ],
        hasSourceMedia: true,
      },
    });

    await waitFor(() => expect(screen.queryByRole("button")).not.toBeInTheDocument());
    expect(fetchOptionalIdentifyQueueItem).not.toHaveBeenCalled();
    expect(fetchIdentifyProviders).not.toHaveBeenCalled();
    expect(requestIdentifySearch).not.toHaveBeenCalled();
  });
});

function eligibleCapabilities(): EntityCapability[] {
  return [{
    kind: CAPABILITY_KIND.files,
    items: [{ role: ENTITY_FILE_ROLE.source, path: "/media/source.mkv", mimeType: "video/x-matroska" }],
  }];
}

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
    cascadeRunning: options.cascadeRunning ?? false,
    createdAt: options.createdAt ?? "2026-05-25T00:00:00Z",
    updatedAt: options.updatedAt ?? "2026-05-25T00:00:00Z",
    completedAt: options.completedAt ?? null,
  };
}
