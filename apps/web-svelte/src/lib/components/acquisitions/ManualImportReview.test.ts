import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import ManualImportReview from "./ManualImportReview.svelte";

describe("manual import presentation", () => {
  it("shows the retained file's verification failure separately from unsafe-file warnings", async () => {
    const failure = "The video could not be decoded completely.";
    render(ManualImportReview, {
      review: { available: true, targets: [], files: [{ sourceRelativePath: "Show.S01E01.mkv",
        name: "Show.S01E01.mkv", sizeBytes: 1000, canMap: true, verificationFailure: failure }] },
      assignments: {}, onAssignmentChange: vi.fn(), onImport: vi.fn(), onReject: vi.fn(),
    });
    await fireEvent.click(screen.getByRole("button", { name: /Downloaded files/ }));
    expect(screen.getByText(failure)).toBeVisible();
    expect(screen.getByText("Verification failed; retrying will verify this file again.")).toBeVisible();
    expect(screen.queryByText("Unsafe file blocked")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Accept and import" })).toBeDisabled();
  });

  it("lets a reviewed movie be accepted despite its quality warning", async () => {
    const onImport = vi.fn();
    render(ManualImportReview, {
      review: { available: true, targets: [{ entityId: "movie", title: "Requested Movie" }],
        files: [{ sourceRelativePath: "chosen.mkv", name: "chosen.mkv", sizeBytes: 1000, canMap: true }] },
      statusMessage: "The video file's measured resolution is lower than the release's claimed quality.",
      assignments: { movie: "chosen.mkv" }, onAssignmentChange: vi.fn(), onImport, onReject: vi.fn(),
    });
    expect(screen.getByRole("button", { name: "Downloaded file for Requested Movie" })).toBeEnabled();
    expect(screen.getByRole("alert")).toHaveTextContent("Import needs attention");
    expect(screen.queryByText("Unsafe file blocked")).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Accept and import" }));
    expect(onImport).toHaveBeenCalledOnce();
  });

  it("offers requested track mappings and prevents acceptance until a file is chosen", async () => {
    const onAssignmentChange = vi.fn();
    render(ManualImportReview, {
      review: { available: true, targets: [{ entityId: "track", title: "Expected Song", position: 1 }],
        files: [{ sourceRelativePath: "unknown.flac", name: "unknown.flac", sizeBytes: 1000, canMap: true }] },
      assignments: {}, onAssignmentChange, onImport: vi.fn(), onReject: vi.fn(),
    });
    expect(screen.getByRole("button", { name: "Accept and import" })).toBeDisabled();
    await fireEvent.keyDown(screen.getByRole("button", { name: "Downloaded file for 01 · Expected Song" }), { key: "ArrowDown" });
    await fireEvent.pointerUp(await screen.findByRole("option", { name: /unknown.flac/ }));
    expect(onAssignmentChange).toHaveBeenCalledWith("track", "unknown.flac");
  });

  it("explains an unsupported mapping once without calling a movie an episode", () => {
    const message = "This download cannot be mapped to individual files.";
    render(ManualImportReview, {
      review: { available: false, files: [], targets: [], message },
      assignments: {}, onAssignmentChange: vi.fn(), onImport: vi.fn(), onReject: vi.fn(),
    });
    expect(screen.getByRole("region", { name: "Downloaded file review" })).toBeInTheDocument();
    expect(screen.getAllByText(message)).toHaveLength(1);
    expect(screen.queryByText("Choose files to import")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Accept and import" })).not.toBeInTheDocument();
  });

  it("keeps the safety warning visible and the full file audit available on demand", async () => {
    const onReject = vi.fn();
    render(ManualImportReview, {
      review: {
        available: false, targets: [], message: "Choose a different release.",
        files: [{ sourceRelativePath: "unexpected.exe", name: "unexpected.exe", sizeBytes: 100, canMap: false, isDangerous: true }],
      },
      statusMessage: "A potentially dangerous file prevented automatic import.",
      assignments: {}, onAssignmentChange: vi.fn(), onImport: vi.fn(), onReject,
    });
    expect(screen.getByRole("alert")).toHaveTextContent("A potentially dangerous file prevented automatic import.");
    const files = screen.getByRole("button", { name: /Downloaded files/ });
    expect(files).toHaveAttribute("aria-expanded", "false");
    await fireEvent.click(files);
    expect(screen.getByText("unexpected.exe")).toBeVisible();
    expect(screen.getByText("Blocked, potentially dangerous")).toBeVisible();
    await fireEvent.click(screen.getByRole("button", { name: "Reject and search again" }));
    expect(onReject).toHaveBeenCalledOnce();
  });
});
