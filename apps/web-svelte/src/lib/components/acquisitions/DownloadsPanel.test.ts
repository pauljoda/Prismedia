import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ACQUISITION_STATUS, ENTITY_KIND, THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
import type { EntityThumbnail, EntityKind } from "$lib/api/generated/model";
import DownloadsPanel from "./DownloadsPanel.svelte";

const mocks = vi.hoisted(() => ({
  deleteAcquisition: vi.fn(),
  fetchDownloadQueue: vi.fn(),
  fetchEntityThumbnails: vi.fn(),
  reSearchAcquisition: vi.fn(),
}));

function hierarchyThumbnail(
  id: string,
  title: string,
  kind: EntityKind,
  parentEntityId: string | null,
  parentKind: EntityKind | null,
  sortOrder: number,
): EntityThumbnail {
  return {
    id,
    title,
    kind,
    parentEntityId,
    parentKind,
    sortOrder,
    coverUrl: null,
    coverThumbUrl: null,
    hoverKind: THUMBNAIL_HOVER_KIND.none,
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: true,
  };
}

vi.mock("$lib/api/acquisitions", () => ({
  deleteAcquisition: mocks.deleteAcquisition,
  fetchDownloadQueue: mocks.fetchDownloadQueue,
  reSearchAcquisition: mocks.reSearchAcquisition,
}));

vi.mock("$lib/api/entities", () => ({
  fetchEntityThumbnails: mocks.fetchEntityThumbnails,
}));

vi.mock("$lib/requests/acquisition-list-item", () => ({
  downloadToListItem: (row: { acquisitionId: string; title?: string }) => ({
    id: row.acquisitionId,
    title: row.title ?? row.acquisitionId,
    tone: "downloading",
    progress: null,
    thumbnail: {},
    statusLabel: "Downloading",
    selectable: true,
  }),
}));

vi.mock("./DownloadManagerTable.svelte", async () => ({
  default: (await import("./DownloadManagerTable.test-stub.svelte")).default,
}));

vi.mock("./AcquisitionPanel.svelte", async () => ({
  default: (await import("./DownloadsAcquisitionPanel.test-stub.svelte")).default,
}));

vi.mock("$lib/components/thumbnails/EntityThumbnail.svelte", async () => ({
  default: (await import("./EntityThumbnail.test-stub.svelte")).default,
}));

vi.mock("$lib/components/entities/ConfirmDialog.svelte", async () => ({
  default: (await import("./ConfirmDialog.test-stub.svelte")).default,
}));

describe("DownloadsPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    mocks.fetchDownloadQueue.mockResolvedValue([
      { acquisitionId: "download-1", entityId: null },
      { acquisitionId: "download-2", entityId: null },
    ]);
    mocks.fetchEntityThumbnails.mockResolvedValue([]);
    mocks.deleteAcquisition
      .mockRejectedValueOnce(new Error("Client refused removal"))
      .mockResolvedValueOnce(undefined);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("continues bulk removal after a failure, reloads, and reports the partial result", async () => {
    render(DownloadsPanel);

    const selectAll = await screen.findByRole("button", { name: "Select all downloads" });
    await waitFor(() => expect(selectAll).toBeEnabled());
    await fireEvent.click(selectAll);
    await fireEvent.click(screen.getByRole("button", { name: "Confirm Remove" }));

    await waitFor(() => expect(mocks.deleteAcquisition).toHaveBeenCalledTimes(2));
    expect(mocks.deleteAcquisition).toHaveBeenNthCalledWith(1, "download-1");
    expect(mocks.deleteAcquisition).toHaveBeenNthCalledWith(2, "download-2");
    expect(mocks.fetchDownloadQueue).toHaveBeenCalledTimes(2);
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Removed 1 of 2 downloads. download-1: Client refused removal",
    );
  });

  it("keeps polling attention-only rows so external reconciliation clears stale downloads", async () => {
    const nativeSetTimeout = globalThis.setTimeout;
    const idlePoll: { run: (() => void) | null } = { run: null };
    vi.spyOn(globalThis, "setTimeout").mockImplementation((handler, delay, ...args) => {
      if (delay === 15_000) {
        idlePoll.run = handler as () => void;
        return 1 as never;
      }
      return nativeSetTimeout(handler, delay, ...args);
    });
    mocks.fetchDownloadQueue
      .mockResolvedValueOnce([
        {
          acquisitionId: "double-life",
          entityId: "double-life-album",
          status: ACQUISITION_STATUS.awaitingSelection,
        },
      ])
      .mockResolvedValueOnce([]);

    render(DownloadsPanel);

    await waitFor(() => expect(mocks.fetchDownloadQueue).toHaveBeenCalledOnce());
    expect(idlePoll.run).not.toBeNull();
    idlePoll.run?.();
    await waitFor(() => expect(mocks.fetchDownloadQueue).toHaveBeenCalledTimes(2));
  });

  it("publishes queue rows only after their Entity hierarchy is ready", async () => {
    let resolveThumbnails!: (value: never[]) => void;
    mocks.fetchDownloadQueue.mockResolvedValueOnce([
      { acquisitionId: "frozen", entityId: "frozen-track" },
    ]);
    mocks.fetchEntityThumbnails.mockReturnValueOnce(new Promise((resolve) => {
      resolveThumbnails = resolve;
    }));

    render(DownloadsPanel);

    await waitFor(() => expect(mocks.fetchDownloadQueue).toHaveBeenCalledOnce());
    expect(screen.getByTestId("entry-count")).toHaveTextContent("0");
    expect(screen.getByRole("button", { name: "Select all downloads" })).toBeDisabled();

    resolveThumbnails([]);
    await waitFor(() => expect(screen.getByTestId("entry-count")).toHaveTextContent("1"));
  });

  it("waits for a slow refresh to finish before scheduling the next active poll", async () => {
    const nativeSetTimeout = globalThis.setTimeout;
    const scheduledPolls: Array<{ delay: number; run: () => void }> = [];
    let resolveRefresh!: (value: never[]) => void;
    vi.spyOn(globalThis, "setTimeout").mockImplementation((handler, delay, ...args) => {
      if (delay === 4_000 || delay === 15_000) {
        scheduledPolls.push({ delay, run: handler as () => void });
        return scheduledPolls.length as never;
      }
      return nativeSetTimeout(handler, delay, ...args);
    });
    mocks.fetchDownloadQueue
      .mockResolvedValueOnce([
        { acquisitionId: "active", entityId: null, status: ACQUISITION_STATUS.downloading },
      ])
      .mockReturnValueOnce(new Promise((resolve) => {
        resolveRefresh = resolve;
      }));

    render(DownloadsPanel);

    await waitFor(() => expect(scheduledPolls.some((poll) => poll.delay === 4_000)).toBe(true));
    const activePoll = scheduledPolls.findLast((poll) => poll.delay === 4_000)!;
    const scheduledBeforeRefresh = scheduledPolls.length;
    activePoll.run();
    await waitFor(() => expect(mocks.fetchDownloadQueue).toHaveBeenCalledTimes(2));
    expect(scheduledPolls).toHaveLength(scheduledBeforeRefresh);

    resolveRefresh([]);
    await waitFor(() => expect(scheduledPolls).toHaveLength(scheduledBeforeRefresh + 1));
  });

  it("resizes the detail pane from the keyboard and persists the chosen share", async () => {
    render(DownloadsPanel);

    const splitter = screen.getByRole("separator", { name: "Resize transfer details" });
    expect(splitter).toHaveAttribute("aria-valuenow", "40");

    await fireEvent.keyDown(splitter, { key: "ArrowUp" });

    expect(splitter).toHaveAttribute("aria-valuenow", "45");
    expect(Number(localStorage.getItem("prismedia.downloads.detail-share"))).toBeCloseTo(0.45);
  });

  it("shows the aggregate detail inspector when an Entity group is selected", async () => {
    mocks.fetchDownloadQueue.mockResolvedValue([
      { acquisitionId: "episode-download", entityId: "episode", title: "Elmo's World" },
    ]);
    mocks.fetchEntityThumbnails.mockImplementation(async (ids: string[]) => ids.flatMap((id) => {
      if (id === "episode") return [hierarchyThumbnail(id, "Elmo's World", ENTITY_KIND.videoEpisode, "season", ENTITY_KIND.videoSeason, 1)];
      if (id === "season") return [hierarchyThumbnail(id, "Season 1", ENTITY_KIND.videoSeason, "series", ENTITY_KIND.videoSeries, 1)];
      if (id === "series") return [hierarchyThumbnail(id, "Sesame Street", ENTITY_KIND.videoSeries, null, null, 0)];
      return [];
    }));

    render(DownloadsPanel);
    await waitFor(() => expect(mocks.fetchEntityThumbnails).toHaveBeenCalledTimes(3));
    await fireEvent.click(screen.getByRole("button", { name: "Inspect series downloads" }));

    expect(await screen.findByText("Sesame Street")).toBeInTheDocument();
    expect(screen.getByText("1 transfer across this Entity")).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Inspect Season 1" }));
    expect(await screen.findByText("Season 1")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Inspect Elmo's World" })).toBeInTheDocument();
  });
});
