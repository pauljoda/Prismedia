import { describe, expect, it } from "vitest";
import { ACQUISITION_STATUS } from "$lib/api/generated/codes";
import {
  ACTIVE_ACQUISITION_STATUSES,
  acquisitionStatusIsKnown,
  acquisitionStatusLabel,
  acquisitionStatusShouldPoll,
} from "./acquisition-status";
import { acquisitionStatusDisplay } from "./acquisition-status-display";

describe("acquisition status", () => {
  it("labels every acquisition state for user-facing status surfaces", () => {
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.pending)).toBe("Pending");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.searching)).toBe("Searching");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.awaitingSelection)).toBe("Choose release");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.queued)).toBe("Queued");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.downloading)).toBe("Downloading");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.downloaded)).toBe("Downloaded");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.importing)).toBe("Importing");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.imported)).toBe("Imported");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.stopping)).toBe("Cleaning up");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.failed)).toBe("Failed");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.cancelled)).toBe("Cancelled");
    expect(acquisitionStatusLabel(ACQUISITION_STATUS.manualImportRequired)).toBe("Manual import");
  });

  it("polls only while an acquisition is actively progressing", () => {
    expect(ACTIVE_ACQUISITION_STATUSES).toEqual([
      ACQUISITION_STATUS.pending,
      ACQUISITION_STATUS.searching,
      ACQUISITION_STATUS.queued,
      ACQUISITION_STATUS.downloading,
      ACQUISITION_STATUS.downloaded,
      ACQUISITION_STATUS.importing,
      ACQUISITION_STATUS.stopping,
    ]);
    expect(ACTIVE_ACQUISITION_STATUSES).not.toContain(ACQUISITION_STATUS.awaitingSelection);
    expect(ACTIVE_ACQUISITION_STATUSES).not.toContain(ACQUISITION_STATUS.imported);
  });

  it("presents destructive cleanup as neutral in-progress work", () => {
    expect(acquisitionStatusDisplay(ACQUISITION_STATUS.stopping)).toMatchObject({
      label: "Cleaning up",
      tone: "cleanup",
    });
    expect(acquisitionStatusShouldPoll(ACQUISITION_STATUS.stopping)).toBe(true);
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
