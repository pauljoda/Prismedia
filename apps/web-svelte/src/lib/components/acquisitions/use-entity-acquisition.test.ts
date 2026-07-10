import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  ACQUISITION_STATUS,
  CAPABILITY_KIND,
  ENTITY_KIND,
  MONITOR_PRESET,
  MONITOR_STATUS,
  THUMBNAIL_HOVER_KIND,
} from "$lib/api/generated/codes";
import type { AcquisitionDetail, EntityKind } from "$lib/api/generated/model";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import Harness from "./use-entity-acquisition.test-harness.svelte";

const mocks = vi.hoisted(() => ({
  commitEntityRequest: vi.fn(),
  fetchAcquisitionForEntity: vi.fn(),
  fetchEntityMonitor: vi.fn(),
  fetchMonitorEligibility: vi.fn(),
  resumeMonitor: vi.fn(),
  startEntityMonitor: vi.fn(),
  stopMonitor: vi.fn(),
}));

vi.mock("$lib/api/acquisitions", () => ({
  fetchAcquisitionForEntity: mocks.fetchAcquisitionForEntity,
}));

vi.mock("$lib/api/monitors", () => ({
  fetchEntityMonitor: mocks.fetchEntityMonitor,
  fetchMonitorEligibility: mocks.fetchMonitorEligibility,
  resumeMonitor: mocks.resumeMonitor,
  startEntityMonitor: mocks.startEntityMonitor,
  stopMonitor: mocks.stopMonitor,
}));

vi.mock("$lib/api/requests", () => ({
  commitEntityRequest: mocks.commitEntityRequest,
  requestMissingChildren: vi.fn(),
  syncContainerRequest: vi.fn(),
}));

describe("useEntityAcquisition", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchEntityMonitor.mockResolvedValue(null);
    mocks.fetchMonitorEligibility.mockResolvedValue(null);
    mocks.stopMonitor.mockResolvedValue({ entityPruned: false });
    mocks.commitEntityRequest.mockResolvedValue({
      containerEntityId: null,
      items: [{
        externalId: "tmdbseason:season-1",
        title: "Season 1",
        outcome: "requested",
        entityId: "season-1",
        acquisitionId: "acquisition-2",
      }],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    cleanup();
  });

  it("keeps the Acquisition surface available for managed on-disk files", async () => {
    mocks.fetchAcquisitionForEntity.mockResolvedValue(null);

    render(Harness, {
      entityId: "book-1",
      capabilities: [{
        kind: CAPABILITY_KIND.fileManagement,
        canDeleteFiles: true,
      }],
    });

    await waitFor(() => {
      expect(mocks.fetchAcquisitionForEntity).toHaveBeenCalledOnce();
      expect(screen.getByTestId("visible")).toHaveTextContent("yes");
    });
  });

  it("refreshes only acquisition state after searching an existing entity", async () => {
    const onChanged = vi.fn(async () => {});
    mocks.fetchAcquisitionForEntity
      .mockResolvedValueOnce(null)
      .mockResolvedValueOnce(acquisition("acquisition-2"));

    render(Harness, { entityId: "season-1", onChanged });
    await waitFor(() => expect(mocks.fetchAcquisitionForEntity).toHaveBeenCalledOnce());

    await fireEvent.click(screen.getByRole("button", { name: "Search for release" }));

    await waitFor(() => {
      expect(screen.getByTestId("acquisition-id")).toHaveTextContent("acquisition-2");
    });
    expect(mocks.commitEntityRequest).toHaveBeenCalledWith("season-1");
    expect(onChanged).not.toHaveBeenCalled();
  });

  it("surfaces an unmonitor cleanup failure instead of silently keeping monitoring on", async () => {
    mocks.fetchAcquisitionForEntity.mockResolvedValue(null);
    mocks.fetchEntityMonitor.mockResolvedValue({
      id: "series-monitor",
      kind: ENTITY_KIND.videoSeries,
      acquisitionId: null,
      status: MONITOR_STATUS.active,
      title: "Series",
      author: null,
      acquisitionStatus: null,
      createdAt: "2026-07-09T00:00:00Z",
      updatedAt: "2026-07-09T00:00:00Z",
      entityId: "series-1",
      preset: MONITOR_PRESET.all,
    });
    mocks.fetchMonitorEligibility.mockResolvedValue({ canMonitor: true, trackableProviders: ["tmdb"] });
    mocks.stopMonitor.mockRejectedValue(new Error("Pending downloads could not be removed"));

    render(Harness, { entityId: "series-1" });
    const toggle = await screen.findByRole("button", { name: "Monitoring" });
    await fireEvent.click(toggle);

    expect(await screen.findByRole("alert")).toHaveTextContent("Pending downloads could not be removed");
    expect(toggle).toHaveTextContent("Monitoring");
  });

  it("keeps a successful monitor mutation while reporting a separate owner refresh failure", async () => {
    const onChanged = vi.fn(async () => {
      throw new Error("Detail reload failed");
    });
    mocks.fetchAcquisitionForEntity.mockResolvedValue(null);
    mocks.fetchMonitorEligibility.mockResolvedValue({ canMonitor: true, trackableProviders: ["tmdb"] });
    mocks.startEntityMonitor.mockResolvedValue(activeMonitor("series-1", null));

    render(Harness, { entityId: "series-1", onChanged });
    await fireEvent.click(await screen.findByRole("button", { name: "Monitor" }));

    expect(await screen.findByRole("button", { name: "Monitoring" })).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Monitoring was updated, but this page could not refresh: Detail reload failed",
    );
  });

  it("retries stop only for an exact unmonitor Stopping status and never resumes it", async () => {
    let finishRefresh!: (value: AcquisitionDetail | null) => void;
    mocks.fetchAcquisitionForEntity
      .mockResolvedValueOnce(acquisition("acquisition-1"))
      .mockImplementationOnce(() => new Promise<AcquisitionDetail | null>((resolve) => {
        finishRefresh = resolve;
      }));
    mocks.fetchEntityMonitor
      .mockResolvedValueOnce({
      id: "series-monitor",
      kind: ENTITY_KIND.videoSeries,
      acquisitionId: null,
      status: MONITOR_STATUS.stopping,
      title: "Series",
      author: null,
      acquisitionStatus: null,
      createdAt: "2026-07-09T00:00:00Z",
      updatedAt: "2026-07-09T00:00:00Z",
      entityId: "series-1",
      preset: MONITOR_PRESET.all,
      })
      .mockResolvedValueOnce(null);
    mocks.fetchMonitorEligibility.mockResolvedValue({ canMonitor: true, trackableProviders: ["tmdb"] });
    mocks.stopMonitor.mockResolvedValue({ entityPruned: false });

    render(Harness, { entityId: "series-1" });
    await waitFor(() => {
      expect(screen.getByTestId("acquisition-id")).toHaveTextContent("acquisition-1");
    });
    const button = await screen.findByRole("button", { name: "Finish unmonitoring" });

    await fireEvent.click(button);
    await waitFor(() => expect(mocks.stopMonitor).toHaveBeenCalledWith("series-monitor"));
    expect(screen.getByTestId("acquisition-id")).toHaveTextContent("none");
    expect(mocks.resumeMonitor).not.toHaveBeenCalled();
    finishRefresh(null);
    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Monitor" })).toBeInTheDocument();
    });
  });

  it("locks a Delete-files monitor without exposing unmonitor retry or resume", async () => {
    mocks.fetchAcquisitionForEntity.mockResolvedValue(acquisition("acquisition-1"));
    mocks.fetchEntityMonitor.mockResolvedValue({
      ...activeMonitor("season-1", "acquisition-1"),
      status: MONITOR_STATUS.deletingFiles,
    });

    render(Harness, { entityId: "season-1" });

    const button = await screen.findByRole("button", { name: "Deleting files…" });
    expect(button).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Finish unmonitoring" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Resume monitoring" })).not.toBeInTheDocument();
    await fireEvent.click(button);
    expect(mocks.stopMonitor).not.toHaveBeenCalled();
    expect(mocks.resumeMonitor).not.toHaveBeenCalled();
  });

  it("fails closed for an unknown monitor status without choosing a destructive action", async () => {
    mocks.fetchAcquisitionForEntity.mockResolvedValue(null);
    mocks.fetchEntityMonitor.mockResolvedValue({
      ...activeMonitor("season-1", null),
      status: null as never,
    });

    render(Harness, { entityId: "season-1" });

    const button = await screen.findByRole("button", { name: "Updating…" });
    expect(button).toBeDisabled();
    await fireEvent.click(button);
    expect(mocks.stopMonitor).not.toHaveBeenCalled();
    expect(mocks.resumeMonitor).not.toHaveBeenCalled();
  });

  it("unmounts a deleted acquisition before waiting for the post-stop refresh", async () => {
    let finishRefresh!: (value: AcquisitionDetail | null) => void;
    const onChanged = vi.fn(async () => {});
    mocks.fetchAcquisitionForEntity
      .mockResolvedValueOnce(acquisition("acquisition-1"))
      .mockImplementationOnce(() => new Promise<AcquisitionDetail | null>((resolve) => {
        finishRefresh = resolve;
      }));
    mocks.fetchEntityMonitor
      .mockResolvedValueOnce(activeMonitor("season-1", "acquisition-1"))
      .mockResolvedValueOnce(null);
    mocks.stopMonitor.mockResolvedValue({ entityPruned: false });

    render(Harness, { entityId: "season-1", onChanged });
    await waitFor(() => {
      expect(screen.getByTestId("acquisition-id")).toHaveTextContent("acquisition-1");
    });
    await fireEvent.click(await screen.findByRole("button", { name: "Monitoring" }));

    await waitFor(() => expect(mocks.fetchAcquisitionForEntity).toHaveBeenCalledTimes(2));
    expect(screen.getByTestId("acquisition-id")).toHaveTextContent("none");
    expect(onChanged).not.toHaveBeenCalled();

    finishRefresh(null);
    await waitFor(() => expect(onChanged).toHaveBeenCalledOnce());
  });

  it("navigates a pruned entity without re-fetching its deleted detail state", async () => {
    const onChanged = vi.fn(async () => {});
    const onPruned = vi.fn(async () => {});
    mocks.fetchAcquisitionForEntity.mockResolvedValue(acquisition("acquisition-1"));
    mocks.fetchEntityMonitor.mockResolvedValue(activeMonitor("season-1", "acquisition-1"));
    mocks.stopMonitor.mockResolvedValue({ entityPruned: true });

    render(Harness, { entityId: "season-1", onChanged, onPruned });
    await waitFor(() => {
      expect(screen.getByTestId("acquisition-id")).toHaveTextContent("acquisition-1");
    });
    await fireEvent.click(await screen.findByRole("button", { name: "Monitoring" }));

    await waitFor(() => expect(onPruned).toHaveBeenCalledOnce());
    expect(screen.getByTestId("acquisition-id")).toHaveTextContent("none");
    expect(mocks.fetchAcquisitionForEntity).toHaveBeenCalledOnce();
    expect(onChanged).not.toHaveBeenCalled();
  });

  it("offers provider sync only for monitored grouping entities", async () => {
    mocks.fetchAcquisitionForEntity.mockResolvedValue(null);
    mocks.fetchEntityMonitor.mockResolvedValue(activeMonitor("series-1", null));
    mocks.fetchMonitorEligibility.mockResolvedValue({
      canMonitor: true,
      trackableProviders: ["tmdb"],
      discoversChildren: true,
      canSearchMissingChildren: true,
      missingChildEntityKind: ENTITY_KIND.videoSeason,
    });

    render(Harness, { entityId: "series-1" });

    await waitFor(() => expect(screen.getByTestId("show-sync")).toHaveTextContent("yes"));
  });

  it("does not offer provider sync for a monitored leaf", async () => {
    mocks.fetchAcquisitionForEntity.mockResolvedValue(null);
    mocks.fetchEntityMonitor.mockResolvedValue(activeMonitor("season-1", null));
    mocks.fetchMonitorEligibility.mockResolvedValue({
      canMonitor: true,
      trackableProviders: ["tmdb"],
      discoversChildren: false,
      canSearchMissingChildren: true,
      missingChildEntityKind: ENTITY_KIND.video,
    });

    render(Harness, { entityId: "season-1" });

    await waitFor(() => expect(screen.getByTestId("show-sync")).toHaveTextContent("no"));
  });

  it("counts only the server-declared missing child kind in a mixed series", async () => {
    mocks.fetchAcquisitionForEntity.mockResolvedValue(null);
    mocks.fetchEntityMonitor.mockResolvedValue(activeMonitor("series-1", null));
    mocks.fetchMonitorEligibility.mockResolvedValue({
      canMonitor: true,
      trackableProviders: ["tmdb"],
      discoversChildren: true,
      canSearchMissingChildren: true,
      missingChildEntityKind: ENTITY_KIND.videoSeason,
    });

    render(Harness, {
      entityId: "series-1",
      childCards: [
        wantedChild("season-1", ENTITY_KIND.videoSeason, "series-1"),
        wantedChild("sub-series", ENTITY_KIND.videoSeries, "series-1"),
        wantedChild("special-1", ENTITY_KIND.video, "series-1"),
      ],
    });

    await waitFor(() => expect(screen.getByTestId("missing-count")).toHaveTextContent("1"));
    expect(screen.getByTestId("show-search-missing")).toHaveTextContent("yes");
  });

  it("keeps audio child monitoring without inventing Search Missing for albums", async () => {
    mocks.fetchAcquisitionForEntity.mockResolvedValue(null);
    mocks.fetchEntityMonitor.mockResolvedValue(activeMonitor("album-1", null));
    mocks.fetchMonitorEligibility.mockResolvedValue({
      canMonitor: true,
      trackableProviders: ["musicbrainz"],
      discoversChildren: false,
      canSearchMissingChildren: false,
      missingChildEntityKind: null,
    });

    render(Harness, {
      entityId: "album-1",
      childCards: [wantedChild("disc-1", ENTITY_KIND.audioLibrary, "album-1")],
    });

    await waitFor(() => expect(screen.getByTestId("show-search-missing")).toHaveTextContent("no"));
    expect(screen.getByTestId("missing-count")).toHaveTextContent("0");
  });

  it("refreshes active child acquisition cards while their editor is collapsed", async () => {
    vi.useFakeTimers();
    const onChanged = vi.fn(async () => {});
    mocks.fetchAcquisitionForEntity.mockResolvedValue(null);

    render(Harness, {
      entityId: "series-1",
      onChanged,
      childCards: [{
        ...wantedChild("season-1", ENTITY_KIND.videoSeason, "series-1"),
        wantedStatus: ACQUISITION_STATUS.downloading,
      }],
    });

    await Promise.resolve();
    await Promise.resolve();
    await vi.advanceTimersByTimeAsync(5000);

    expect(onChanged).toHaveBeenCalled();
  });
});

function wantedChild(id: string, kind: EntityKind, parentEntityId: string): EntityThumbnailCard {
  return {
    entity: {
      id,
      kind,
      title: id,
      parentEntityId,
      sortOrder: null,
      capabilities: [{
        kind: CAPABILITY_KIND.flags,
        isFavorite: null,
        isNsfw: null,
        isOrganized: null,
        isWanted: true,
      } as never],
      childrenByKind: [],
      relationships: [],
    },
    aspectRatio: "poster",
    cover: null,
    hover: { kind: THUMBNAIL_HOVER_KIND.none },
    wantedStatus: null,
  };
}

function activeMonitor(entityId: string, acquisitionId: string | null) {
  return {
    id: "monitor-1",
    kind: ENTITY_KIND.videoSeason,
    acquisitionId,
    status: MONITOR_STATUS.active,
    title: "Season 1",
    author: null,
    acquisitionStatus: acquisitionId ? ACQUISITION_STATUS.searching : null,
    createdAt: "2026-07-09T00:00:00Z",
    updatedAt: "2026-07-09T00:00:00Z",
    entityId,
    preset: MONITOR_PRESET.all,
  };
}

function acquisition(id: string): AcquisitionDetail {
  return {
    summary: {
      id,
      status: ACQUISITION_STATUS.searching,
      statusMessage: null,
      title: "Season 1",
      author: null,
      series: "Bluey",
      year: 2018,
      posterUrl: null,
      progress: null,
      createdAt: "2026-07-09T00:00:00Z",
      updatedAt: "2026-07-09T00:00:00Z",
      kind: ENTITY_KIND.videoSeason,
      entityId: "season-1",
    },
    candidates: [],
  };
}
