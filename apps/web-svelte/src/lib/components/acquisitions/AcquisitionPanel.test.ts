import { cleanup, fireEvent, render, waitFor, within } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  ACQUISITION_STATUS,
  DOWNLOAD_PROTOCOL,
  ENTITY_KIND,
} from "$lib/api/generated/codes";
import type { AcquisitionDetail, ReleaseCandidateView } from "$lib/api/generated/model";
import AcquisitionPanel from "./AcquisitionPanel.svelte";
import AcquisitionPanelFeedbackTestHarness from "./AcquisitionPanel.feedback-test-harness.svelte";

const mocks = vi.hoisted(() => ({
  fetchAcquisition: vi.fn(),
  fetchAcquisitionFiles: vi.fn(),
  fetchAcquisitionHistory: vi.fn(),
  retryAcquisitionImport: vi.fn(),
  reSearchAcquisition: vi.fn(),
  queueAcquisitionCandidate: vi.fn(),
  deleteAcquisition: vi.fn(),
  goto: vi.fn(),
}));

vi.mock("$app/navigation", () => ({ goto: mocks.goto }));
vi.mock("$app/paths", () => ({ resolve: (path: string) => path }));

vi.mock("$lib/api/acquisitions", () => ({
  blocklistAcquisitionCandidate: vi.fn(),
  cancelAcquisition: vi.fn(),
  deleteAcquisition: mocks.deleteAcquisition,
  fetchAcquisition: mocks.fetchAcquisition,
  fetchAcquisitionFiles: mocks.fetchAcquisitionFiles,
  fetchAcquisitionHistory: mocks.fetchAcquisitionHistory,
  fetchAcquisitionTransfer: vi.fn(),
  queueAcquisitionCandidate: mocks.queueAcquisitionCandidate,
  reSearchAcquisition: mocks.reSearchAcquisition,
  retryAcquisitionImport: mocks.retryAcquisitionImport,
  uploadManualTorrent: vi.fn(),
}));

vi.mock("$lib/components/entities/ConfirmDialog.svelte", async () => ({
  default: (await import("./ConfirmDialog.test-stub.svelte")).default,
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

  it("does not reload when refreshed detail is published back through its owner", async () => {
    const waiting = acquisition(ACQUISITION_STATUS.waitingForDownloadClient);
    let resolveInitialLoad!: (detail: AcquisitionDetail) => void;
    mocks.fetchAcquisition
      .mockImplementationOnce(() => new Promise<AcquisitionDetail>((resolve) => {
        resolveInitialLoad = resolve;
      }))
      // Hold a feedback-triggered second request open so the pre-fix loop remains bounded.
      .mockImplementation(() => new Promise<AcquisitionDetail>(() => {}));

    render(AcquisitionPanelFeedbackTestHarness, { initialDetail: waiting });

    await waitFor(() => expect(mocks.fetchAcquisition).toHaveBeenCalledOnce());
    await waitFor(() => expect(mocks.fetchAcquisitionHistory).toHaveBeenCalledOnce());

    resolveInitialLoad({
      ...waiting,
      summary: { ...waiting.summary },
    });
    await new Promise((resolve) => setTimeout(resolve, 25));

    expect(mocks.fetchAcquisition).toHaveBeenCalledOnce();
    expect(mocks.fetchAcquisitionHistory).toHaveBeenCalledOnce();
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

  it("lets a completed download retry when automatic import handoff is stranded", async () => {
    const downloaded = acquisition(ACQUISITION_STATUS.downloaded);
    mocks.fetchAcquisition.mockResolvedValue(downloaded);
    mocks.retryAcquisitionImport.mockResolvedValue(downloaded);

    const view = render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: downloaded,
    });

    const retry = await view.findByRole("button", { name: "Retry import" });
    await fireEvent.click(retry);

    expect(mocks.retryAcquisitionImport).toHaveBeenCalledWith("acquisition-1", false);
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

  it("allows long acquisition filenames to wrap at any character", async () => {
    const downloaded = acquisition(ACQUISITION_STATUS.downloaded);
    const filename = "Frozen.[2013].Soundtrack.[Deluxe.Edition].[Christophe.Beck].".repeat(4) + "flac";
    mocks.fetchAcquisition.mockResolvedValue(downloaded);
    mocks.fetchAcquisitionFiles.mockResolvedValue({
      imported: false,
      files: [{ name: filename, sizeBytes: 1234, progress: 1 }],
    });

    const view = render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: downloaded,
    });

    const fileLabel = await view.findByText(filename);
    expect(fileLabel).not.toHaveClass("truncate");
    expect(fileLabel).toHaveClass("whitespace-normal", "[overflow-wrap:anywhere]");
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

  it("offers a confirmed destructive start-over for a failed durable checkpoint", async () => {
    const failed = acquisition(ACQUISITION_STATUS.failed, true);
    const onReset = vi.fn();
    mocks.fetchAcquisition.mockResolvedValue(failed);
    mocks.deleteAcquisition.mockResolvedValue(undefined);

    const view = render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: failed,
      onReset,
    });

    await fireEvent.click(await view.findByRole("button", { name: "Start over" }));
    const dialog = view.getByRole("dialog", { name: "Start this acquisition over?" });
    expect(within(dialog).getByText(/deletes every file owned by the interrupted import/i)).toBeInTheDocument();
    await fireEvent.click(within(dialog).getByRole("button", { name: "Confirm Start over" }));

    await waitFor(() => expect(mocks.deleteAcquisition).toHaveBeenCalledWith("acquisition-1"));
    expect(onReset).toHaveBeenCalledOnce();
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

  it("only prompts for a date after the provider returned no release date", async () => {
    const waiting = acquisition(ACQUISITION_STATUS.waitingForRelease);
    waiting.summary.releaseDateMetadataUnavailable = true;
    waiting.summary.statusMessage = "The configured metadata provider did not return a streaming release date. This item is waiting: check again later, search manually, or enter the date yourself.";
    mocks.fetchAcquisition.mockResolvedValue(waiting);
    mocks.reSearchAcquisition.mockResolvedValue(acquisition(ACQUISITION_STATUS.searching));

    const view = render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: waiting,
    });

    expect(await view.findAllByText("Waiting for release")).toHaveLength(2);
    expect(view.getAllByText(/provider did not return/i)).toHaveLength(2);
    await fireEvent.click(view.getByRole("button", { name: "Enter release date" }));
    expect(mocks.goto).toHaveBeenCalledWith("/?edit=dates#entity-dates-editor");
    await fireEvent.click(view.getByRole("button", { name: "Manual search" }));
    expect(mocks.reSearchAcquisition).toHaveBeenCalledWith("acquisition-1", undefined);
  });

  it("does not prompt for a manual date while the initial provider check is pending", async () => {
    const waiting = acquisition(ACQUISITION_STATUS.waitingForRelease);
    waiting.summary.statusMessage = "No streaming release date was included in the request metadata. Checking the configured provider once before asking you to choose what to do.";
    mocks.fetchAcquisition.mockResolvedValue(waiting);

    const view = render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: waiting,
    });

    expect(await view.findAllByText("Waiting for release")).toHaveLength(2);
    expect(view.queryByRole("button", { name: "Enter release date" })).toBeNull();
    expect(view.getByRole("button", { name: "Manual search" })).toBeInTheDocument();
  });

  it("submits an exact custom term from release review", async () => {
    const awaiting = acquisition(ACQUISITION_STATUS.awaitingSelection);
    mocks.fetchAcquisition.mockResolvedValue(awaiting);
    mocks.reSearchAcquisition.mockResolvedValue(acquisition(ACQUISITION_STATUS.searching));
    const view = render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: awaiting,
    });

    const input = await view.findByRole("searchbox", { name: "Custom release search term" });
    await fireEvent.input(input, { target: { value: "director cut remux" } });
    await fireEvent.click(view.getByRole("button", { name: "Search term" }));

    expect(mocks.reSearchAcquisition).toHaveBeenCalledWith("acquisition-1", "director cut remux");
  });

  it("keeps a queue failure visible after refreshing the durable server state", async () => {
    const cancelled = acquisition(ACQUISITION_STATUS.cancelled);
    cancelled.candidates = [candidate()];
    mocks.fetchAcquisition.mockResolvedValue(cancelled);
    mocks.queueAcquisitionCandidate.mockRejectedValue(
      new Error("SABnzbd could not be reached at localhost:8090"),
    );

    const view = render(AcquisitionPanel, {
      acquisitionId: "acquisition-1",
      detail: cancelled,
    });

    await waitFor(() => expect(mocks.fetchAcquisition).toHaveBeenCalledOnce());
    await fireEvent.click(view.getAllByRole("button", { name: "Download" })[0]);

    expect(await view.findByRole("alert")).toHaveTextContent(
      "SABnzbd could not be reached at localhost:8090",
    );
    await waitFor(() => expect(mocks.fetchAcquisition).toHaveBeenCalledTimes(2));
    expect(view.getByRole("alert")).toHaveTextContent(
      "SABnzbd could not be reached at localhost:8090",
    );
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

function candidate(): ReleaseCandidateView {
  return {
    id: "release-1",
    indexerName: "Prowlarr",
    title: "Avatar: The Last Airbender",
    sizeBytes: 2_500_000_000,
    seeders: 4,
    peers: 2,
    protocol: DOWNLOAD_PROTOCOL.usenet,
    accepted: true,
    score: 100,
    rejections: [],
    infoUrl: null,
    publishedAt: null,
  };
}
