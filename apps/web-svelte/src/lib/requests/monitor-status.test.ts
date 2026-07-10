import { describe, expect, it } from "vitest";
import { MONITOR_STATUS } from "$lib/api/generated/codes";
import {
  monitorHasUnknownStatus,
  monitorIsDeletingFiles,
  monitorIsStopping,
  monitorTransitionIsLocked,
} from "./monitor-status";

describe("monitor status semantics", () => {
  it("keeps Delete-files distinct from retryable unmonitor cleanup", () => {
    const deleting = { status: MONITOR_STATUS.deletingFiles };
    const stopping = { status: MONITOR_STATUS.stopping };

    expect(monitorIsDeletingFiles(deleting)).toBe(true);
    expect(monitorIsStopping(deleting)).toBe(false);
    expect(monitorIsStopping(stopping)).toBe(true);
    expect(monitorIsDeletingFiles(stopping)).toBe(false);
    expect(monitorTransitionIsLocked(deleting)).toBe(true);
    expect(monitorTransitionIsLocked(stopping)).toBe(true);
  });

  it("fails closed for a status newer than this client understands", () => {
    const unknown = { status: null as never };

    expect(monitorHasUnknownStatus(unknown)).toBe(true);
    expect(monitorIsStopping(unknown)).toBe(false);
    expect(monitorTransitionIsLocked(unknown)).toBe(true);
  });

  it("keeps active, paused, and fulfilled statuses outside transition locks", () => {
    for (const status of [MONITOR_STATUS.active, MONITOR_STATUS.paused, MONITOR_STATUS.fulfilled]) {
      const monitor = { status };
      expect(monitorHasUnknownStatus(monitor)).toBe(false);
      expect(monitorTransitionIsLocked(monitor)).toBe(false);
    }
  });
});
