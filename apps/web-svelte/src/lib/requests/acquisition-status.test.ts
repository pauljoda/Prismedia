import { describe, expect, it } from "vitest";
import { ACQUISITION_STATUS } from "$lib/api/generated/codes";
import {
  ACTIVE_ACQUISITION_STATUSES,
  acquisitionStatusIsKnown,
  acquisitionStatusLabel,
  acquisitionStatusShouldPoll,
} from "./acquisition-status";
import { acquisitionStatusDisplay } from "./acquisition-status-display";
import { CloudDownload, FolderInput, PackageCheck } from "@lucide/svelte";

describe("acquisition status", () => {
  it("labels every acquisition state for user-facing status surfaces", () => {
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.pending)).toBe("Pending");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.searching)).toBe("Searching");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.waitingForRelease)).toBe("Waiting for release");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.manualSearchRequired)).toBe("Waiting for release");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.awaitingSelection)).toBe("Choose release");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.queued)).toBe("Queued");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.downloading)).toBe("Downloading");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.waitingForDownloadClient)).toBe("Waiting for download client");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.downloaded)).toBe("Downloaded");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.importing)).toBe("Importing");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.imported)).toBe("Imported");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.stopping)).toBe("Cleaning up");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.failed)).toBe("Failed");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.cancelled)).toBe("Cancelled");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.manualImportRequired)).toBe("Review import");
  });

  it("polls only while an acquisition is actively progressing", () => {
    expect(ACTIVE_ACQUISITION_STATUSES).toEqual([
      ACQUISITION_STATUS.pending,
      ACQUISITION_STATUS.searching,
      ACQUISITION_STATUS.queued,
      ACQUISITION_STATUS.downloading,
      ACQUISITION_STATUS.waitingForDownloadClient,
      ACQUISITION_STATUS.downloaded,
      ACQUISITION_STATUS.importing,
      ACQUISITION_STATUS.stopping,
    ]);
    expect(ACTIVE_ACQUISITION_STATUSES).not.toContain(ACQUISITION_STATUS.awaitingSelection);
    expect(ACTIVE_ACQUISITION_STATUSES).not.toContain(ACQUISITION_STATUS.waitingForRelease);
    expect(ACTIVE_ACQUISITION_STATUSES).not.toContain(ACQUISITION_STATUS.manualSearchRequired);
    expect(ACTIVE_ACQUISITION_STATUSES).not.toContain(ACQUISITION_STATUS.imported);
  });

  it("presents release waits without treating them as a hot polling state", () => {
    expect(acquisitionStatusDisplay(ACQUISITION_STATUS.waitingForRelease)).toMatchObject({
      label: "Waiting for release",
      tone: "queued",
    });
    expect(acquisitionStatusShouldPoll(ACQUISITION_STATUS.waitingForRelease)).toBe(false);
  });

  it("presents legacy unavailable release metadata as a release wait", () => {
    expect(acquisitionStatusDisplay(ACQUISITION_STATUS.manualSearchRequired)).toMatchObject({
      label: "Waiting for release",
      tone: "queued",
    });
    expect(acquisitionStatusShouldPoll(ACQUISITION_STATUS.manualSearchRequired)).toBe(false);
  });

  it("presents destructive cleanup as neutral in-progress work", () => {
    expect(acquisitionStatusDisplay(ACQUISITION_STATUS.stopping)).toMatchObject({
      label: "Cleaning up",
      tone: "cleanup",
    });
    expect(acquisitionStatusShouldPoll(ACQUISITION_STATUS.stopping)).toBe(true);
  });

  it("presents an unhealthy download client as retryable waiting work", () => {
    expect(acquisitionStatusDisplay(ACQUISITION_STATUS.waitingForDownloadClient)).toMatchObject({
      label: "Waiting for download client",
      tone: "queued",
    });
    expect(acquisitionStatusShouldPoll(ACQUISITION_STATUS.waitingForDownloadClient)).toBe(true);
  });

  it.each(Object.values(ACQUISITION_STATUS))("uses the same status label in compact and full views for %s", (status) => {
    expect(acquisitionStatusDisplay(status).label).toBe(acquisitionStatusLabel(status));
  });

  it("distinguishes transfer, downloaded files, and library import", () => {
    expect(acquisitionStatusDisplay(ACQUISITION_STATUS.downloading)).toMatchObject({ label: "Downloading", icon: CloudDownload });
    expect(acquisitionStatusDisplay(ACQUISITION_STATUS.downloaded)).toMatchObject({ label: "Downloaded", icon: PackageCheck });
    expect(acquisitionStatusDisplay(ACQUISITION_STATUS.importing)).toMatchObject({ label: "Importing", icon: FolderInput });
  });

  it("names the decision that needs attention", () => {
    expect(acquisitionStatusDisplay(ACQUISITION_STATUS.awaitingSelection).label).toBe("Choose release");
    expect(acquisitionStatusDisplay(ACQUISITION_STATUS.manualImportRequired).label).toBe("Review import");
  });

  it("keeps a wanted placeholder distinct from an unknown lifecycle", () => {
    expect(acquisitionStatusDisplay(null)).toMatchObject({ label: "Wanted", tone: "wanted" });
    expect(acquisitionStatusDisplay(undefined)).toMatchObject({ label: "Wanted", tone: "wanted" });
    expect(acquisitionStatusDisplay("toString")).toMatchObject({ label: "Updating", tone: "cleanup" });
  });

  it("fails closed for a newer status until the generated client catches up", () => {
    const unknown = "future-lifecycle-state";

    expect(acquisitionStatusIsKnown(unknown)).toBe(false);
    expect(acquisitionStatusLabel(unknown)).toBe("Updating");
    expect(acquisitionStatusShouldPoll(unknown)).toBe(true);
    expect(acquisitionStatusDisplay(unknown)).toMatchObject({
      label: "Updating",
      tone: "cleanup",
    });
  });
});
