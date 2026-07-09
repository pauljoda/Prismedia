import { describe, expect, it } from "vitest";
import { ACQUISITION_STATUS } from "$lib/api/generated/codes";
import { ACTIVE_ACQUISITION_STATUSES, acquisitionStatusLabel } from "./acquisition-status";

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
      ACQUISITION_STATUS.importing,
    ]);
    expect(ACTIVE_ACQUISITION_STATUSES).not.toContain(ACQUISITION_STATUS.awaitingSelection);
    expect(ACTIVE_ACQUISITION_STATUSES).not.toContain(ACQUISITION_STATUS.imported);
  });
});
