import { describe, expect, it } from "vitest";
import { JOB_RUN_STATUS, JOB_TYPE } from "$lib/api/generated/codes";
import type { JobRun } from "$lib/api/generated/model";
import { ACTIVITY_WINDOW_HOURS } from "./job-activity";
import { JOB_LANE_SECTION, buildJobLanes, isQuietLane, totalLaneCounts } from "./job-lanes";

const NOW = Date.parse("2026-09-25T12:30:00Z");

function run(id: string, type: string, status: string, finishedAt: string | null): JobRun {
  return {
    id,
    type: type as JobRun["type"],
    status: status as JobRun["status"],
    progress: 0,
    message: null,
    targetKind: null,
    targetId: null,
    targetLabel: null,
    createdAt: "2026-09-25T09:00:00Z",
    startedAt: finishedAt ? "2026-09-25T09:00:00Z" : null,
    finishedAt,
  };
}

describe("buildJobLanes", () => {
  it("folds repeated runs of one type into a single lane with server totals", () => {
    const runs = [
      run("a", JOB_TYPE.acquisitionMonitor, JOB_RUN_STATUS.completed, "2026-09-25T11:00:00Z"),
      run("b", JOB_TYPE.acquisitionMonitor, JOB_RUN_STATUS.completed, "2026-09-25T12:00:00Z"),
      run("c", JOB_TYPE.acquisitionMonitor, JOB_RUN_STATUS.failed, "2026-09-25T12:10:00Z"),
    ];
    const counts = [
      { type: JOB_TYPE.acquisitionMonitor, status: JOB_RUN_STATUS.completed, count: 480 },
      { type: JOB_TYPE.acquisitionMonitor, status: JOB_RUN_STATUS.failed, count: 1 },
    ];

    const [lane] = buildJobLanes(runs, counts, NOW);

    expect(lane?.section).toBe(JOB_LANE_SECTION.acquisition);
    expect(lane?.counts.completed).toBe(480);
    expect(lane?.runs.map((entry) => entry.id)).toEqual(["c", "b", "a"]);
    expect(lane?.buckets).toHaveLength(ACTIVITY_WINDOW_HOURS);
    expect(lane?.buckets.at(-1)).toMatchObject({ total: 2, failed: 1 });
  });

  it("puts live lanes first and folds lanes with no recent runs into the quiet list", () => {
    const runs = [run("s", JOB_TYPE.scanLibrary, JOB_RUN_STATUS.running, null)];
    const counts = [{ type: JOB_TYPE.databaseBackup, status: JOB_RUN_STATUS.completed, count: 12 }];

    const lanes = buildJobLanes(runs, counts, NOW);

    expect(lanes.map((lane) => lane.type)).toEqual([JOB_TYPE.scanLibrary, JOB_TYPE.databaseBackup]);
    expect(lanes[0]?.buckets.at(-1)?.running).toBe(1);
    expect(isQuietLane(lanes[0]!)).toBe(false);
    expect(isQuietLane(lanes[1]!)).toBe(true);
    expect(totalLaneCounts(lanes)).toMatchObject({ running: 1, completed: 12 });
  });
});
