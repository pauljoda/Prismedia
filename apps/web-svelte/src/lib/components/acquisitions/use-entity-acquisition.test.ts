import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ACQUISITION_STATUS, ENTITY_KIND } from "$lib/api/generated/codes";
import type { AcquisitionDetail } from "$lib/api/generated/model";
import Harness from "./use-entity-acquisition.test-harness.svelte";

const mocks = vi.hoisted(() => ({
  commitEntityRequest: vi.fn(),
  fetchAcquisitionForEntity: vi.fn(),
  fetchEntityMonitor: vi.fn(),
  fetchMonitorEligibility: vi.fn(),
}));

vi.mock("$lib/api/acquisitions", () => ({
  fetchAcquisitionForEntity: mocks.fetchAcquisitionForEntity,
}));

vi.mock("$lib/api/monitors", () => ({
  fetchEntityMonitor: mocks.fetchEntityMonitor,
  fetchMonitorEligibility: mocks.fetchMonitorEligibility,
  resumeMonitor: vi.fn(),
  startEntityMonitor: vi.fn(),
  stopMonitor: vi.fn(),
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

  afterEach(cleanup);

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
});

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
