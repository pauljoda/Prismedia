import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import DownloadsPanel from "./DownloadsPanel.svelte";

const mocks = vi.hoisted(() => ({
  deleteAcquisition: vi.fn(),
  fetchDownloadQueue: vi.fn(),
  fetchEntityThumbnails: vi.fn(),
  reSearchAcquisition: vi.fn(),
}));

vi.mock("$lib/api/acquisitions", () => ({
  deleteAcquisition: mocks.deleteAcquisition,
  fetchDownloadQueue: mocks.fetchDownloadQueue,
  reSearchAcquisition: mocks.reSearchAcquisition,
}));

vi.mock("$lib/api/entities", () => ({
  fetchEntityThumbnails: mocks.fetchEntityThumbnails,
}));

vi.mock("$lib/requests/acquisition-list-item", () => ({
  downloadToListItem: (row: { acquisitionId: string }) => ({ id: row.acquisitionId }),
  Trash2: vi.fn(),
}));

vi.mock("./AcquisitionListShell.svelte", async () => ({
  default: (await import("./AcquisitionListShell.test-stub.svelte")).default,
}));

vi.mock("$lib/components/entities/ConfirmDialog.svelte", async () => ({
  default: (await import("./ConfirmDialog.test-stub.svelte")).default,
}));

describe("DownloadsPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchDownloadQueue.mockResolvedValue([
      { acquisitionId: "download-1", entityId: null },
      { acquisitionId: "download-2", entityId: null },
    ]);
    mocks.fetchEntityThumbnails.mockResolvedValue([]);
    mocks.deleteAcquisition
      .mockRejectedValueOnce(new Error("Client refused removal"))
      .mockResolvedValueOnce(undefined);
  });

  afterEach(cleanup);

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
});
