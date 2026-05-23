import { describe, expect, it } from "vitest";
import { buildJobsDashboard, mapJobRun } from "./jobs-dashboard";
import type { JobRun } from "$lib/api/prismedia";

const baseJob: JobRun = {
  id: "job-1",
  type: "scan-library",
  status: "queued",
  progress: 0,
  message: null,
  targetKind: null,
  targetId: null,
  targetLabel: null,
  createdAt: "2026-05-13T12:00:00Z",
  startedAt: null,
  finishedAt: null,
};

describe("jobs dashboard adapter", () => {
  it("maps job lifecycle codes into the existing jobs page run shape", () => {
    expect(mapJobRun({ ...baseJob, status: "queued" }).status).toBe("waiting");
    expect(mapJobRun({ ...baseJob, status: "running" }).status).toBe("active");
    expect(mapJobRun({ ...baseJob, status: "completed" }).status).toBe("completed");
    expect(mapJobRun({ ...baseJob, status: "failed", message: "boom" }).error).toBe("boom");
  });

  it("builds queue summaries and dashboard buckets from jobs", () => {
    const dashboard = buildJobsDashboard([
      { ...baseJob, id: "queued", type: "scan-library", status: "queued" },
      { ...baseJob, id: "running", type: "import-metadata", status: "running", progress: 40 },
      { ...baseJob, id: "failed", type: "refresh-collection", status: "failed", message: "bad" },
      { ...baseJob, id: "done", type: "noop", status: "completed", finishedAt: "2026-05-13T12:01:00Z" },
    ]);

    expect(dashboard.queues.find((queue) => queue.name === "library-scan")).toMatchObject({
      active: 0,
      waiting: 1,
      backlog: 1,
      status: "active",
    });
    expect(dashboard.activeJobs.map((job) => job.id)).toEqual(["running"]);
    expect(dashboard.failedJobs.map((job) => job.id)).toEqual(["failed"]);
    expect(dashboard.completedJobs.map((job) => job.id)).toEqual(["done"]);
    expect(dashboard.recentJobs).toHaveLength(4);
  });
});
