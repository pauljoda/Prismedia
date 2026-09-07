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
    expect(screen.getByRole("button", { name: "Import mapped episodes" })).toBeDisabled();
  });

  it("explains an unsupported mapping once without calling a movie an episode", () => {
    const message = "This download cannot be mapped to individual files.";
    render(ManualImportReview, {
      review: { available: false, files: [], targets: [], message },
      assignments: {}, onAssignmentChange: vi.fn(), onImport: vi.fn(), onReject: vi.fn(),
    });
    expect(screen.getByRole("region", { name: "Downloaded file review" })).toBeInTheDocument();
    expect(screen.getAllByText(message)).toHaveLength(1);
    expect(screen.queryByText("Map expected episodes")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Import mapped episodes" })).not.toBeInTheDocument();
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
