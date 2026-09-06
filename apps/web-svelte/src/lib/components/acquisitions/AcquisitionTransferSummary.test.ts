import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, describe, expect, it } from "vitest";
import AcquisitionTransferSummary from "./AcquisitionTransferSummary.svelte";

afterEach(cleanup);

describe("Acquisition transfer summary", () => {
  it("shows useful progress first and keeps the piece map behind a disclosure", async () => {
    render(AcquisitionTransferSummary, { transfer: {
      stage: "Downloading", active: true, percent: 42,
      speed: "8 MB/s", eta: "2m", size: "2 GB", peers: "4 / 12", pieces: [2, 1, 0],
    } });
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "42");
    expect(screen.getByText("42%")).toBeInTheDocument();
    expect(screen.getByText("8 MB/s")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Transfer details" })).toHaveAttribute("aria-expanded", "false");
    await fireEvent.click(screen.getByRole("button", { name: "Transfer details" }));
    expect(screen.getByRole("button", { name: "Transfer details" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByLabelText("Download piece map")).toBeInTheDocument();
    expect(screen.getByText("4 / 12")).toBeInTheDocument();
  });

  it("shows connection feedback without inventing zero percent progress", () => {
    render(AcquisitionTransferSummary, { transfer: null });
    expect(screen.getByText("Waiting for download progress")).toBeInTheDocument();
    expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
  });

  it("keeps paused progress visible without a running spinner", () => {
    const { container } = render(AcquisitionTransferSummary, { transfer: {
      stage: "Paused", active: false, percent: 42,
      speed: "0 B/s", eta: "—", size: "2 GB", peers: "0 / 0", pieces: [],
    } });
    expect(screen.getByText("Paused")).toBeInTheDocument();
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "42");
    expect(container.querySelector(".animate-spin")).toBeNull();
  });
});
