import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import ManualAcquisitionActions from "./ManualAcquisitionActions.svelte";

const mocks = vi.hoisted(() => ({
  searchManualReplacement: vi.fn(),
  queueManualReplacement: vi.fn(),
  uploadAcquisitionContent: vi.fn(),
}));

vi.mock("$lib/api/acquisitions", () => mocks);

describe("ManualAcquisitionActions", () => {
  beforeEach(() => vi.clearAllMocks());

  it("opens a replacement review without selecting a release", async () => {
    mocks.searchManualReplacement.mockResolvedValue({ searchId: "review-1", candidates: [] });

    render(ManualAcquisitionActions, {
      entityId: "entity-1",
      canReplace: true,
      canUpload: true,
      onStarted: vi.fn(),
    });
    await fireEvent.click(screen.getByRole("button", { name: "Replace" }));

    await waitFor(() => expect(mocks.searchManualReplacement).toHaveBeenCalledWith("entity-1", undefined));
    expect(mocks.queueManualReplacement).not.toHaveBeenCalled();
    expect(screen.getByText("Nothing changes until you select a release.")).toBeInTheDocument();
  });

  it("uses the user's exact replacement term", async () => {
    mocks.searchManualReplacement.mockResolvedValue({ searchId: "review-1", candidates: [] });
    render(ManualAcquisitionActions, {
      entityId: "entity-1",
      canReplace: true,
      canUpload: false,
      onStarted: vi.fn(),
    });
    await fireEvent.click(screen.getByRole("button", { name: "Replace" }));
    const input = await screen.findByRole("searchbox", { name: "Custom replacement search term" });
    await fireEvent.input(input, { target: { value: "criterion 2160p" } });
    await fireEvent.click(screen.getByRole("button", { name: "Search term" }));

    await waitFor(() => expect(mocks.searchManualReplacement).toHaveBeenLastCalledWith("entity-1", "criterion 2160p"));
  });

  it("reports browser upload progress before handing off to import", async () => {
    let finishUpload!: (detail: unknown) => void;
    mocks.uploadAcquisitionContent.mockImplementation(
      (_entityId: string, _files: File[], onProgress: (progress: number) => void) => {
        onProgress(0.42);
        return new Promise((resolve) => (finishUpload = resolve));
      },
    );
    const onStarted = vi.fn();
    render(ManualAcquisitionActions, {
      entityId: "entity-1",
      canReplace: false,
      canUpload: true,
      onStarted,
    });

    const input = screen.getByLabelText("Upload content");
    await fireEvent.change(input, { target: { files: [new File(["content"], "release.zip")] } });

    expect(await screen.findByRole("status", { name: "Uploading 42%" })).toBeInTheDocument();
    finishUpload({ summary: { id: "acquisition-1" } });
    await waitFor(() => expect(onStarted).toHaveBeenCalledOnce());
  });
});
