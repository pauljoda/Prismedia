import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ACQUISITION_STATUS, ENTITY_KIND } from "$lib/api/generated/codes";
import type { AcquisitionDetail } from "$lib/api/generated/model";
import Harness from "./EntityAcquisitionCard.test-harness.svelte";

vi.mock("$lib/components/acquisitions/AcquisitionPanel.svelte", async () => ({
  default: (await import("./AcquisitionPanel.test-stub.svelte")).default,
}));

vi.mock("$lib/components/entities/ConfirmDialog.svelte", async () => ({
  default: (await import("./ConfirmDialog.test-stub.svelte")).default,
}));

describe("EntityAcquisitionCard", () => {
  afterEach(cleanup);

  it("keeps managed file deletion in the Entity acquisition toolbar without an acquisition row", () => {
    render(Harness, {
      initialAcquisition: null,
      refresh: vi.fn(async () => {}),
      showFileManagement: true,
    });

    expect(screen.getByRole("button", { name: "Delete files" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Notify imported" })).not.toBeInTheDocument();
  });

  it("forwards an imported transition to the owning entity page", async () => {
    const onImported = vi.fn();

    render(Harness, {
      initialAcquisition: acquisition("acquisition-1"),
      refresh: vi.fn(async () => {}),
      onImported,
    });

    await fireEvent.click(screen.getByRole("button", { name: "Notify imported" }));

    expect(onImported).toHaveBeenCalledOnce();
  });

  it("offers a stop retry for durable unmonitor cleanup without exposing Resume", async () => {
    const onToggleMonitor = vi.fn(async () => {});
    render(Harness, {
      initialAcquisition: acquisition("acquisition-1"),
      refresh: vi.fn(async () => {}),
      monitorStopping: true,
      onToggleMonitor,
    });

    expect(screen.queryByRole("button", { name: "Resume monitoring" })).toBeNull();
    await fireEvent.click(screen.getByRole("button", { name: "Finish unmonitoring" }));
    expect(onToggleMonitor).toHaveBeenCalledOnce();
  });

  it("renders Delete-files as a locked checked state without exposing unmonitor retry", async () => {
    const onToggleMonitor = vi.fn(async () => {});
    render(Harness, {
      initialAcquisition: acquisition("acquisition-1"),
      refresh: vi.fn(async () => {}),
      monitorDeletingFiles: true,
      onToggleMonitor,
    });

    const button = screen.getByRole("button", { name: "Deleting files…" });
    expect(button).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Finish unmonitoring" })).toBeNull();
    expect(onToggleMonitor).not.toHaveBeenCalled();
  });

  it("fails closed when the monitor status is unknown", async () => {
    render(Harness, {
      initialAcquisition: acquisition("acquisition-1"),
      refresh: vi.fn(async () => {}),
      monitorUnknownStatus: true,
    });

    const button = screen.getByRole("button", { name: "Updating…" });
    expect(button).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Finish unmonitoring" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Resume monitoring" })).toBeNull();
  });
});

function acquisition(id: string): AcquisitionDetail {
  return {
    summary: {
      id,
      status: ACQUISITION_STATUS.imported,
      statusMessage: null,
      title: "Season 1",
      author: null,
      series: "Bluey",
      year: 2018,
      posterUrl: null,
      progress: 1,
      createdAt: "2026-07-09T00:00:00Z",
      updatedAt: "2026-07-09T00:00:00Z",
      kind: ENTITY_KIND.videoSeason,
      entityId: "season-1",
    },
    candidates: [],
  };
}
