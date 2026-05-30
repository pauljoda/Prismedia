import { describe, expect, it } from "vitest";
import { describeWorkerHealth, workerOfflineTooltip } from "./worker-health";

describe("worker health presentation", () => {
  it("shows checking before the first heartbeat response", () => {
    expect(describeWorkerHealth(null)).toMatchObject({
      status: "checking",
      label: "Worker checking",
      led: "warning",
    });
  });

  it("shows online when the worker heartbeat endpoint reports online", () => {
    expect(
      describeWorkerHealth({
        status: "online",
        workerId: "worker-1",
        lastSeenAt: "2026-05-24T09:00:00Z",
        staleAfterSeconds: 45,
      }),
    ).toMatchObject({
      status: "online",
      label: "Worker online",
      led: "phosphor",
    });
  });

  it("shows self-healing guidance when the worker is offline", () => {
    const status = describeWorkerHealth({
      status: "offline",
      workerId: null,
      lastSeenAt: null,
      staleAfterSeconds: 45,
    });

    expect(status).toMatchObject({
      status: "offline",
      label: "Worker offline",
      led: "error",
      tooltip: workerOfflineTooltip,
    });
  });
});
