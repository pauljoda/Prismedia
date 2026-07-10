import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  ACQUISITION_STATUS,
  CAPABILITY_KIND,
  ENTITY_FILE_ROLE,
  ENTITY_KIND,
  MONITOR_PRESET,
  MONITOR_STATUS,
} from "$lib/api/generated/codes";
import type { AcquisitionSummary, MonitorView } from "$lib/api/generated/model";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import EntityChildMonitoring from "./EntityChildMonitoring.svelte";

const mocks = vi.hoisted(() => ({
  commitEntityRequest: vi.fn(),
  fetchEntityMonitorStates: vi.fn(),
  resumeMonitor: vi.fn(),
  startEntityMonitor: vi.fn(),
  startMonitor: vi.fn(),
  stopMonitor: vi.fn(),
}));

vi.mock("$lib/api/monitors", () => ({
  fetchEntityMonitorStates: mocks.fetchEntityMonitorStates,
  resumeMonitor: mocks.resumeMonitor,
  startEntityMonitor: mocks.startEntityMonitor,
  startMonitor: mocks.startMonitor,
  stopMonitor: mocks.stopMonitor,
}));

vi.mock("$lib/api/requests", () => ({
  commitEntityRequest: mocks.commitEntityRequest,
}));

describe("EntityChildMonitoring", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchEntityMonitorStates.mockResolvedValue([]);
    mocks.commitEntityRequest.mockResolvedValue({ containerEntityId: null, items: [] });
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("uses stable Entity monitoring instead of a historical acquisition row", async () => {
    mocks.fetchEntityMonitorStates.mockResolvedValue([
      entityState("season-1", {
        canMonitor: true,
        latestAcquisition: acquisition("acq-season", "season-1"),
      }),
    ]);
    mocks.startEntityMonitor.mockResolvedValue(monitor("monitor-season", "season-1", null));
    const onChanged = vi.fn(async () => {});

    render(EntityChildMonitoring, {
      cards: [childCard("season-1", ENTITY_KIND.videoSeason, "Season 1", false, true, true)],
      onChanged,
    });

    await expand();
    expect(mocks.fetchEntityMonitorStates).toHaveBeenCalledWith(["season-1"]);
    const toggle = await screen.findByRole("switch", { name: "Monitor Season 1" });
    expect(toggle).not.toBeDisabled();
    expect(screen.getByText("Imported · Not monitored")).toBeInTheDocument();
    await fireEvent.click(toggle);

    await waitFor(() => expect(mocks.startEntityMonitor).toHaveBeenCalledWith("season-1"));
    expect(mocks.commitEntityRequest).not.toHaveBeenCalled();
    expect(mocks.startMonitor).not.toHaveBeenCalled();
    expect(onChanged).toHaveBeenCalledOnce();
  });

  it("uses a neutral toolbar label for mixed requestable child kinds", async () => {
    render(EntityChildMonitoring, {
      cards: [
        childCard("season-1", ENTITY_KIND.videoSeason, "Season 1", false, true),
        childCard("special-1", ENTITY_KIND.video, "Special", false, true),
      ],
    });

    await expand();

    expect(screen.getByText("Items")).toBeInTheDocument();
    expect(screen.queryByText("Video Season")).not.toBeInTheDocument();
  });

  it("prefers immediate request search for a Wanted child that is also provider-monitorable", async () => {
    const wanted = childCard("book-1", ENTITY_KIND.book, "Wanted Book", true);
    mocks.fetchEntityMonitorStates.mockResolvedValue([
      entityState("book-1", { canRequest: true, canMonitor: true }),
    ]);

    render(EntityChildMonitoring, { cards: [wanted] });

    await expand();
    await fireEvent.click(await screen.findByRole("switch", { name: "Monitor Wanted Book" }));

    await waitFor(() => expect(mocks.commitEntityRequest).toHaveBeenCalledWith("book-1"));
    expect(mocks.startMonitor).not.toHaveBeenCalled();
    expect(mocks.startEntityMonitor).not.toHaveBeenCalled();
  });

  it("uses authoritative server eligibility to monitor a source-backed child without an acquisition", async () => {
    mocks.startEntityMonitor.mockResolvedValue(monitor("artist-monitor", "artist-1", null));
    mocks.fetchEntityMonitorStates.mockResolvedValue([entityState("artist-1", { canMonitor: true })]);

    render(EntityChildMonitoring, {
      cards: [childCard("artist-1", ENTITY_KIND.musicArtist, "Child Artist", false, true, true)],
    });

    await expand();
    const toggle = await screen.findByRole("switch", { name: "Monitor Child Artist" });
    await fireEvent.click(toggle);

    await waitFor(() => expect(mocks.startEntityMonitor).toHaveBeenCalledWith("artist-1"));
    expect(mocks.startMonitor).not.toHaveBeenCalled();
    expect(mocks.commitEntityRequest).not.toHaveBeenCalled();
  });

  it("keeps a legacy unbound source-backed child honestly unavailable", async () => {
    render(EntityChildMonitoring, {
      cards: [childCard("book-legacy", ENTITY_KIND.book, "Legacy Book", false, true)],
    });

    await expand();
    const toggle = await screen.findByRole("switch", { name: "Monitor Legacy Book" });
    expect(toggle).toBeDisabled();
    expect(screen.getByText("On disk")).toBeInTheDocument();
    expect(mocks.startEntityMonitor).not.toHaveBeenCalled();
  });

  it("does not use a historical acquisition as authority for a new monitor", async () => {
    mocks.fetchEntityMonitorStates.mockResolvedValue([
      entityState("book-history", {
        latestAcquisition: acquisition("imported-acquisition", "book-history"),
      }),
    ]);

    render(EntityChildMonitoring, {
      cards: [childCard("book-history", ENTITY_KIND.book, "Historical Book", false, true)],
    });

    await expand();
    const toggle = await screen.findByRole("switch", { name: "Monitor Historical Book" });
    expect(toggle).toBeDisabled();
    expect(screen.getByText("On disk")).toBeInTheDocument();
    expect(mocks.startMonitor).not.toHaveBeenCalled();
    expect(mocks.startEntityMonitor).not.toHaveBeenCalled();
  });

  it("does not treat a provider-bound child as monitorable when its plugin route is disabled", async () => {
    mocks.fetchEntityMonitorStates.mockResolvedValue([entityState("book-disabled")]);

    render(EntityChildMonitoring, {
      cards: [childCard("book-disabled", ENTITY_KIND.book, "Disabled Provider Book", false, true, true)],
    });

    await expand();
    const toggle = await screen.findByRole("switch", { name: "Monitor Disabled Provider Book" });
    expect(toggle).toBeDisabled();
    expect(screen.getByText("On disk")).toBeInTheDocument();
    expect(mocks.startEntityMonitor).not.toHaveBeenCalled();
  });

  it("stops the exact child monitor when monitoring is switched off", async () => {
    mocks.fetchEntityMonitorStates.mockResolvedValue([entityState("book-1", {
      latestAcquisition: acquisition("acq-book", "book-1"),
      monitor: monitor("book-monitor", "book-1", "acq-book"),
    })]);

    render(EntityChildMonitoring, {
      cards: [childCard("book-1", ENTITY_KIND.book, "Monitored Book")],
    });

    await expand();
    await fireEvent.click(await screen.findByRole("switch", { name: "Monitor Monitored Book" }));

    await waitFor(() => expect(mocks.stopMonitor).toHaveBeenCalledWith("book-monitor"));
  });

  it("uses a stable Entity monitor after an imported acquisition is detached", async () => {
    mocks.fetchEntityMonitorStates.mockResolvedValue([entityState("book-1", {
      canMonitor: true,
      latestAcquisition: acquisition("acq-book", "book-1"),
      monitor: monitor("stable-book-monitor", "book-1", null),
    })]);

    render(EntityChildMonitoring, {
      cards: [childCard("book-1", ENTITY_KIND.book, "Stable Book", false, true, true)],
    });

    await expand();
    const toggle = await screen.findByRole("switch", { name: "Monitor Stable Book" });
    expect(toggle).toHaveAttribute("aria-checked", "true");
    expect(screen.getByText("Imported · Monitoring")).toBeInTheDocument();
    await fireEvent.click(toggle);

    await waitFor(() => expect(mocks.stopMonitor).toHaveBeenCalledWith("stable-book-monitor"));
  });

  it("continues a bulk update after one child fails and reports the partial failure", async () => {
    mocks.fetchEntityMonitorStates.mockResolvedValue([
      entityState("book-1", { canMonitor: true, latestAcquisition: acquisition("acq-one", "book-1") }),
      entityState("book-2", { canMonitor: true, latestAcquisition: acquisition("acq-two", "book-2") }),
    ]);
    mocks.startEntityMonitor
      .mockRejectedValueOnce(new Error("Indexer unavailable"))
      .mockResolvedValueOnce(monitor("monitor-two", "book-2", null));

    render(EntityChildMonitoring, {
      cards: [
        childCard("book-1", ENTITY_KIND.book, "First Book"),
        childCard("book-2", ENTITY_KIND.book, "Second Book"),
      ],
    });

    await expand();
    const section = screen.getByRole("region", { name: "Child monitoring" });
    await fireEvent.click(within(section).getByRole("button", { name: "Monitor all" }));

    await waitFor(() => expect(mocks.startEntityMonitor).toHaveBeenCalledTimes(2));
    expect(await within(section).findByText(/1 updated; 1 failed/i)).toBeInTheDocument();
    expect(within(section).getByText(/First Book: Indexer unavailable/i)).toBeInTheDocument();
  });

  it("keeps a successful child mutation successful when the owning page refresh fails", async () => {
    mocks.fetchEntityMonitorStates.mockResolvedValue([
      entityState("book-1", { canMonitor: true, latestAcquisition: acquisition("acq-book", "book-1") }),
    ]);
    mocks.startEntityMonitor.mockResolvedValue(monitor("monitor-book", "book-1", null));
    const onChanged = vi.fn(async () => {
      throw new Error("Parent page unavailable");
    });

    render(EntityChildMonitoring, {
      cards: [childCard("book-1", ENTITY_KIND.book, "Book One")],
      onChanged,
    });

    await expand();
    await fireEvent.click(await screen.findByRole("switch", { name: "Monitor Book One" }));

    await waitFor(() => expect(mocks.startEntityMonitor).toHaveBeenCalledOnce());
    await waitFor(() => expect(mocks.fetchEntityMonitorStates).toHaveBeenCalledTimes(2));
    expect(screen.queryByText(/Book One: Failed to update monitoring/i)).not.toBeInTheDocument();
    expect(await screen.findByText(/Monitoring updated.*page details/i)).toBeInTheDocument();
  });

  it("reconciles bulk mutations even when the owning page refresh fails", async () => {
    mocks.fetchEntityMonitorStates.mockResolvedValue([
      entityState("book-1", { canMonitor: true, latestAcquisition: acquisition("acq-book", "book-1") }),
    ]);
    mocks.startEntityMonitor.mockResolvedValue(monitor("monitor-book", "book-1", null));
    const onChanged = vi.fn(async () => {
      throw new Error("Parent page unavailable");
    });

    render(EntityChildMonitoring, {
      cards: [childCard("book-1", ENTITY_KIND.book, "Book One")],
      onChanged,
    });

    await expand();
    await fireEvent.click(screen.getByRole("button", { name: "Monitor all" }));

    await waitFor(() => expect(mocks.startEntityMonitor).toHaveBeenCalledOnce());
    await waitFor(() => expect(mocks.fetchEntityMonitorStates).toHaveBeenCalledTimes(2));
    expect(await screen.findByText(/1 item updated.*page details/i)).toBeInTheDocument();
  });

  it("exposes retry only for an exact unmonitor Stopping claim", async () => {
    mocks.fetchEntityMonitorStates.mockResolvedValue([entityState("book-1", {
      latestAcquisition: acquisition("acq-book", "book-1"),
      monitor: {
        ...monitor("book-monitor", "book-1", "acq-book"),
        status: MONITOR_STATUS.stopping,
      },
    })]);

    render(EntityChildMonitoring, {
      cards: [childCard("book-1", ENTITY_KIND.book, "Stopping Book")],
    });

    await expand();
    const toggle = await screen.findByRole("switch", { name: "Monitor Stopping Book" });
    expect(toggle).toBeDisabled();
    expect(screen.getByText("Stopping…")).toBeInTheDocument();
    await fireEvent.click(toggle);
    expect(mocks.resumeMonitor).not.toHaveBeenCalled();
    expect(mocks.stopMonitor).not.toHaveBeenCalled();

    await fireEvent.click(screen.getByRole("button", { name: "Retry cleanup" }));
    await waitFor(() => expect(mocks.stopMonitor).toHaveBeenCalledWith("book-monitor"));
    expect(mocks.resumeMonitor).not.toHaveBeenCalled();
  });

  it("keeps Delete-files intent checked and locked without exposing unmonitor retry", async () => {
    mocks.fetchEntityMonitorStates.mockResolvedValue([entityState("book-1", {
      latestAcquisition: acquisition("acq-book", "book-1"),
      monitor: {
        ...monitor("book-monitor", "book-1", "acq-book"),
        status: MONITOR_STATUS.deletingFiles,
      },
    })]);

    render(EntityChildMonitoring, {
      cards: [childCard("book-1", ENTITY_KIND.book, "Deleting Book")],
    });

    await expand();
    const toggle = await screen.findByRole("switch", { name: "Monitor Deleting Book" });
    expect(toggle).toHaveAttribute("aria-checked", "true");
    expect(toggle).toBeDisabled();
    expect(screen.getByText("Deleting files…")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry cleanup" })).not.toBeInTheDocument();
    expect(mocks.stopMonitor).not.toHaveBeenCalled();
    expect(mocks.resumeMonitor).not.toHaveBeenCalled();
  });

  it("locks an acquisition claimed for destructive cleanup without exposing a retry action", async () => {
    mocks.fetchEntityMonitorStates.mockResolvedValue([entityState("book-1", {
      latestAcquisition: {
        ...acquisition("acq-book", "book-1"),
        status: ACQUISITION_STATUS.stopping,
      },
    })]);

    render(EntityChildMonitoring, {
      cards: [childCard("book-1", ENTITY_KIND.book, "Stopping Book")],
    });

    await expand();
    const toggle = await screen.findByRole("switch", { name: "Monitor Stopping Book" });
    expect(toggle).toBeDisabled();
    expect(screen.getByText("Stopping…")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry cleanup" })).not.toBeInTheDocument();
    expect(mocks.startMonitor).not.toHaveBeenCalled();
    expect(mocks.stopMonitor).not.toHaveBeenCalled();
  });

  it("fails closed for an unknown monitor status without choosing a cleanup action", async () => {
    mocks.fetchEntityMonitorStates.mockResolvedValue([entityState("book-1", {
      monitor: {
        ...monitor("book-monitor", "book-1", null),
        status: null as never,
      },
    })]);

    render(EntityChildMonitoring, {
      cards: [childCard("book-1", ENTITY_KIND.book, "Future State Book")],
    });

    await expand();
    const toggle = await screen.findByRole("switch", { name: "Monitor Future State Book" });
    expect(toggle).toHaveAttribute("aria-checked", "true");
    expect(toggle).toBeDisabled();
    expect(screen.getByText("Updating…")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry cleanup" })).not.toBeInTheDocument();
    expect(mocks.stopMonitor).not.toHaveBeenCalled();
    expect(mocks.resumeMonitor).not.toHaveBeenCalled();
  });

  it("reconciles child rows when the owning Entity graph changes while expanded", async () => {
    const view = render(EntityChildMonitoring, {
      cards: [childCard("book-1", ENTITY_KIND.book, "First Book")],
    });
    await expand();
    expect(await screen.findByText("First Book")).toBeInTheDocument();

    await view.rerender({
      cards: [
        childCard("book-1", ENTITY_KIND.book, "First Book"),
        childCard("book-2", ENTITY_KIND.book, "Second Book"),
      ],
    });

    await waitFor(() => expect(mocks.fetchEntityMonitorStates).toHaveBeenCalledTimes(2));
    expect(mocks.fetchEntityMonitorStates).toHaveBeenLastCalledWith(["book-1", "book-2"]);
    expect(await screen.findByText("Second Book")).toBeInTheDocument();
  });

  it("reloads the same children every time the section is expanded", async () => {
    render(EntityChildMonitoring, {
      cards: [childCard("book-1", ENTITY_KIND.book, "First Book")],
    });

    const disclosure = screen.getByRole("button", { name: /Child monitoring/ });
    expect(disclosure).toHaveAttribute("aria-expanded", "false");
    await expand();
    expect(disclosure).toHaveAttribute("aria-expanded", "true");
    await fireEvent.click(disclosure);
    expect(disclosure).toHaveAttribute("aria-expanded", "false");
    await fireEvent.click(disclosure);

    await waitFor(() => expect(mocks.fetchEntityMonitorStates).toHaveBeenCalledTimes(2));
  });

  it("polls while expanded so same-id monitor state changes reconcile", async () => {
    const poll: { run: (() => void) | null } = { run: null };
    vi.spyOn(globalThis, "setInterval").mockImplementation((handler, delay) => {
      if (delay === 5_000) poll.run = handler as () => void;
      return 1 as never;
    });
    const clearIntervalSpy = vi.spyOn(globalThis, "clearInterval")
      .mockImplementation(() => undefined);
    mocks.fetchEntityMonitorStates.mockResolvedValue([
      entityState("book-1", { canMonitor: true }),
    ]);

    render(EntityChildMonitoring, {
      cards: [childCard("book-1", ENTITY_KIND.book, "First Book", false, true, true)],
    });
    await expand();
    const toggle = await screen.findByRole("switch", { name: "Monitor First Book" });
    expect(toggle).toHaveAttribute("aria-checked", "false");

    mocks.fetchEntityMonitorStates.mockResolvedValue([
      entityState("book-1", {
        canMonitor: true,
        monitor: monitor("book-monitor", "book-1", null),
      }),
    ]);
    poll.run?.();

    await waitFor(() => expect(mocks.fetchEntityMonitorStates).toHaveBeenCalledTimes(2));
    expect(toggle).toHaveAttribute("aria-checked", "true");
    expect(screen.getByText("Monitoring")).toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: /Child monitoring/ }));
    expect(clearIntervalSpy).toHaveBeenCalledWith(1);
  });

  it("refreshes the owning Entity graph when a child import becomes ready", async () => {
    const poll: { run: (() => void) | null } = { run: null };
    vi.spyOn(globalThis, "setInterval").mockImplementation((handler, delay) => {
      if (delay === 5_000) poll.run = handler as () => void;
      return 1 as never;
    });
    const downloading = {
      ...acquisition("acq-book", "book-1"),
      status: ACQUISITION_STATUS.downloading,
    };
    mocks.fetchEntityMonitorStates.mockResolvedValue([
      entityState("book-1", { latestAcquisition: downloading }),
    ]);
    const onChanged = vi.fn(async () => {});

    render(EntityChildMonitoring, {
      cards: [childCard("book-1", ENTITY_KIND.book, "First Book", true)],
      onChanged,
    });
    await expand();
    expect(onChanged).not.toHaveBeenCalled();

    mocks.fetchEntityMonitorStates.mockResolvedValue([
      entityState("book-1", { latestAcquisition: acquisition("acq-book", "book-1") }),
    ]);
    poll.run?.();

    await waitFor(() => expect(mocks.fetchEntityMonitorStates).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(onChanged).toHaveBeenCalledOnce());
  });
});

async function expand(): Promise<void> {
  await fireEvent.click(screen.getByRole("button", { name: /Child monitoring/ }));
  await waitFor(() => expect(mocks.fetchEntityMonitorStates).toHaveBeenCalledOnce());
}

function childCard(
  id: string,
  kind: EntityThumbnailCard["entity"]["kind"],
  title: string,
  wanted = false,
  sourceBacked = false,
  providerBound = false,
): EntityThumbnailCard {
  const capabilities: EntityThumbnailCard["entity"]["capabilities"] = [];
  if (wanted) {
    capabilities.push({
      kind: CAPABILITY_KIND.flags,
      isFavorite: null,
      isNsfw: null,
      isOrganized: null,
      isWanted: true,
    } as never, {
      kind: CAPABILITY_KIND.links,
      externalIds: [{ provider: "provider", value: id, url: null }],
      urls: [],
    } as never);
  }
  if (sourceBacked) {
    capabilities.push({
      kind: CAPABILITY_KIND.files,
      items: [{ role: ENTITY_FILE_ROLE.source, path: `/media/${id}`, mimeType: "application/octet-stream" }],
    });
  }
  if (providerBound) {
    capabilities.push({
      kind: CAPABILITY_KIND.providerIdentity,
      pluginId: "metadata-plugin",
      identityNamespace: "opaque-provider",
      identityValue: id,
      url: null,
    });
  }
  return {
    entity: {
      id,
      kind,
      title,
      parentEntityId: "parent-1",
      sortOrder: null,
      capabilities,
      childrenByKind: [],
      relationships: [],
    },
    aspectRatio: "poster",
    cover: null,
    hover: { kind: "none" },
  };
}

function acquisition(id: string, entityId: string): AcquisitionSummary {
  return {
    id,
    status: ACQUISITION_STATUS.imported,
    statusMessage: null,
    title: entityId,
    author: null,
    series: null,
    year: null,
    posterUrl: null,
    progress: 1,
    createdAt: "2026-07-09T00:00:00Z",
    updatedAt: "2026-07-09T00:00:00Z",
    kind: ENTITY_KIND.book,
    entityId,
  };
}

function monitor(id: string, entityId: string, acquisitionId: string | null): MonitorView {
  return {
    id,
    kind: ENTITY_KIND.book,
    acquisitionId,
    status: MONITOR_STATUS.active,
    title: entityId,
    author: null,
    acquisitionStatus: acquisitionId ? ACQUISITION_STATUS.imported : null,
    createdAt: "2026-07-09T00:00:00Z",
    updatedAt: "2026-07-09T00:00:00Z",
    entityId,
    preset: MONITOR_PRESET.all,
  };
}

function entityState(
  entityId: string,
  overrides: {
    canMonitor?: boolean;
    canRequest?: boolean;
    latestAcquisition?: AcquisitionSummary | null;
    monitor?: MonitorView | null;
  } = {},
) {
  return {
    entityId,
    canMonitor: overrides.canMonitor ?? false,
    canRequest: overrides.canRequest ?? false,
    trackableProviders: overrides.canMonitor ? ["metadata-plugin"] : [],
    discoversChildren: false,
    monitor: overrides.monitor ?? null,
    latestAcquisition: overrides.latestAcquisition ?? null,
  };
}
