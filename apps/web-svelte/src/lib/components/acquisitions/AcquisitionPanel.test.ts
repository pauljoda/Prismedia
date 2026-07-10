import { cleanup, fireEvent, render, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  ACQUISITION_STATUS,
  ENTITY_KIND,
} from "$lib/api/generated/codes";
import type { AcquisitionDetail } from "$lib/api/generated/model";
import AcquisitionPanel from "./AcquisitionPanel.svelte";

const mocks = vi.hoisted(() => ({
  fetchAcquisition: vi.fn(),
  fetchAcquisitionFiles: vi.fn(),
  fetchAcquisitionHistory: vi.fn(),
  retryAcquisitionImport: vi.fn(),
}));

vi.mock("$lib/api/acquisitions", () => ({
  blocklistAcquisitionCandidate: vi.fn(),
  cancelAcquisition: vi.fn(),
  fetchAcquisition: mocks.fetchAcquisition,
  fetchAcquisitionFiles: mocks.fetchAcquisitionFiles,
  fetchAcquisitionHistory: mocks.fetchAcquisitionHistory,
  fetchAcquisitionTransfer: vi.fn(),
  queueAcquisitionCandidate: vi.fn(),
  reSearchAcquisition: vi.fn(),
  retryAcquisitionImport: mocks.retryAcquisitionImport,
  uploadManualTorrent: vi.fn(),
}));

describe("AcquisitionPanel", () => {
  let poll: (() => void | Promise<void>) | null;

  beforeEach(() => {
    vi.clearAllMocks();
    poll = null;
    mocks.fetchAcquisitionFiles.mockResolvedValue({ imported: false, files: [] });
    mocks.fetchAcquisitionHistory.mockResolvedValue([]);
    const originalSetInterval = globalThis.setInterval;
    vi.spyOn(globalThis, "setInterval").mockImplementation((handler, timeout) => {
      if (timeout === 3000) {
        poll = handler as () => void | Promise<void>;
        return 1 as unknown as ReturnType<typeof setInterval>;
      }
      return originalSetInterval(handler, timeout);
    });
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("notifies its owner exactly once when polling observes Imported", async () => {
    const onImported = vi.fn();
    mocks.fetchAcquisition
      .mockResolvedValueOnce(acquisition(ACQUISITION_STATUS.importing))
      .mockResolvedValue(acquisition(ACQUISITION_STATUS.imported));

    render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: acquisition(ACQUISITION_STATUS.importing),
      onImported,
    });

    await waitFor(() => expect(mocks.fetchAcquisition).toHaveBeenCalledOnce());
    await waitFor(() => expect(mocks.fetchAcquisitionFiles).toHaveBeenCalledOnce());
    expect(poll).not.toBeNull();

    await poll?.();
    expect(mocks.fetchAcquisition).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(onImported).toHaveBeenCalledOnce());

    await poll?.();
    expect(onImported).toHaveBeenCalledOnce();
  });

  it("does not report an acquisition that was already Imported on first paint", async () => {
    const onImported = vi.fn();
    mocks.fetchAcquisition.mockResolvedValue(acquisition(ACQUISITION_STATUS.imported));
    mocks.fetchAcquisitionFiles.mockResolvedValue({ imported: true, files: [] });

    render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: acquisition(ACQUISITION_STATUS.imported),
      onImported,
    });

    await waitFor(() => expect(mocks.fetchAcquisition).toHaveBeenCalledOnce());
    expect(onImported).not.toHaveBeenCalled();
  });

  it("keeps polling through Downloaded and reports the following Imported state", async () => {
    const onImported = vi.fn();
    mocks.fetchAcquisition
      .mockResolvedValueOnce(acquisition(ACQUISITION_STATUS.downloaded))
      .mockResolvedValue(acquisition(ACQUISITION_STATUS.imported));

    render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: acquisition(ACQUISITION_STATUS.downloaded),
      onImported,
    });

    await waitFor(() => expect(mocks.fetchAcquisition).toHaveBeenCalledOnce());
    await waitFor(() => expect(mocks.fetchAcquisitionFiles).toHaveBeenCalledOnce());
    expect(poll).not.toBeNull();
    await poll?.();
    await waitFor(() => expect(onImported).toHaveBeenCalledOnce());
  });

  it("reports Imported when the bound detail is advanced by its owner", async () => {
    const onImported = vi.fn();
    mocks.fetchAcquisition.mockResolvedValue(acquisition(ACQUISITION_STATUS.importing));
    const view = render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: acquisition(ACQUISITION_STATUS.importing),
      onImported,
    });

    await waitFor(() => expect(mocks.fetchAcquisition).toHaveBeenCalledOnce());
    await view.rerender({
      acquisitionId: "acquisition-1",
      detail: acquisition(ACQUISITION_STATUS.imported),
      onImported,
    });

    await waitFor(() => expect(onImported).toHaveBeenCalledOnce());
  });

  it("offers exact import resume instead of Search again for a failed durable checkpoint", async () => {
    const failed = acquisition(ACQUISITION_STATUS.failed, true);
    mocks.fetchAcquisition.mockResolvedValue(failed);
    mocks.retryAcquisitionImport.mockResolvedValue(failed);

    const view = render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: failed,
    });

    const retry = await view.findByRole("button", { name: "Retry import" });
    expect(view.queryByRole("button", { name: "Search again" })).toBeNull();
    await fireEvent.click(retry);
    expect(mocks.retryAcquisitionImport).toHaveBeenCalledWith("acquisition-1", false);
  });

  it("does not offer retry while a durable import is already running", async () => {
    const importing = acquisition(ACQUISITION_STATUS.importing, true);
    mocks.fetchAcquisition.mockResolvedValue(importing);

    const view = render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: importing,
    });

    await waitFor(() => expect(mocks.fetchAcquisition).toHaveBeenCalledOnce());
    expect(view.queryByRole("button", { name: "Retry import" })).toBeNull();
    expect(view.queryByRole("button", { name: "Search again" })).toBeNull();
  });

  it("polls cleanup without exposing cancel, search, selection, or import actions", async () => {
    const stopping = acquisition(ACQUISITION_STATUS.stopping, true);
    mocks.fetchAcquisition.mockResolvedValue(stopping);

    const view = render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: stopping,
    });

    await waitFor(() => expect(mocks.fetchAcquisition).toHaveBeenCalledOnce());
    expect(poll).not.toBeNull();
    expect(view.getByText("Cleaning up acquisition")).toBeInTheDocument();
    expect(view.queryByRole("button", { name: "Cancel" })).toBeNull();
    expect(view.queryByRole("button", { name: "Search again" })).toBeNull();
    expect(view.queryByRole("button", { name: "Retry import" })).toBeNull();
    expect(view.queryByRole("button", { name: "Import anyway" })).toBeNull();
    expect(view.queryByText("Releases")).toBeNull();
  });

  it("leaves stable Entity monitoring to the owning acquisition card", async () => {
    const pending = acquisition(ACQUISITION_STATUS.pending);
    mocks.fetchAcquisition.mockResolvedValue(pending);

    const view = render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: pending,
    });

    await waitFor(() => expect(mocks.fetchAcquisition).toHaveBeenCalledOnce());
    expect(view.queryByRole("button", { name: /monitor/i })).toBeNull();
  });

});

function acquisition(
  status: AcquisitionDetail["summary"]["status"],
  hasResumableImport = false,
): AcquisitionDetail {
  return {
    summary: {
      id: "acquisition-1",
      status,
      statusMessage: null,
      title: "Season 1",
      author: null,
      series: "Bluey",
      year: 2018,
      posterUrl: null,
      progress: status === ACQUISITION_STATUS.imported ? 1 : 0.9,
      createdAt: "2026-07-09T00:00:00Z",
      updatedAt: "2026-07-09T00:00:00Z",
      kind: ENTITY_KIND.videoSeason,
      entityId: "season-1",
      hasResumableImport,
    },
    candidates: [],
  };
}
