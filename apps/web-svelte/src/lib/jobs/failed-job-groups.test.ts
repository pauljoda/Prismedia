import { describe, expect, it } from "vitest";
import type { JobRun } from "./models";
import { errorFingerprint, groupFailedJobs } from "./helpers";

describe("failed job grouping", () => {
  it("combines repeated failed jobs with the same queue and error", () => {
    const groups = groupFailedJobs([
      jobRun({ id: "older", error: "Cannot generate trickplay", updatedAt: "2026-05-13T12:00:00Z" }),
      jobRun({ id: "newer", error: "Cannot generate trickplay", updatedAt: "2026-05-13T12:05:00Z" }),
    ]);

    expect(groups).toHaveLength(1);
    expect(groups[0]).toMatchObject({
      fingerprint: errorFingerprint(groups[0].representative),
      count: 2,
    });
    expect(groups[0].representative.id).toBe("newer");
  });

  it("keeps different messages in separate failure groups", () => {
    const groups = groupFailedJobs([
      jobRun({ id: "probe", error: "Probe metadata is missing" }),
      jobRun({ id: "thumbnail", error: "Thumbnail source is missing" }),
    ]);

    expect(groups.map((group) => group.count)).toEqual([1, 1]);
    expect(new Set(groups.map((group) => group.fingerprint)).size).toBe(2);
  });

  it("keeps the same error message in different queues separate", () => {
    const groups = groupFailedJobs([
      jobRun({ id: "video", queueName: "preview", error: "Source file is missing" }),
      jobRun({ id: "image", queueName: "image-thumbnail", error: "Source file is missing" }),
    ]);

    expect(groups).toHaveLength(2);
    expect(groups.map((group) => group.representative.queueName).sort()).toEqual([
      "image-thumbnail",
      "preview",
    ]);
  });

  it("supports suppression by the shared failure fingerprint", () => {
    const groups = groupFailedJobs([
      jobRun({ id: "first", error: "Benign recurring failure" }),
      jobRun({ id: "second", error: "Benign recurring failure" }),
    ]);
    const suppressed = new Set([groups[0].fingerprint]);

    const visible = groups.filter((group) => !suppressed.has(group.fingerprint));

    expect(visible).toHaveLength(0);
  });
});

function jobRun(overrides: Partial<JobRun>): JobRun {
  return {
    id: "job-1",
    jobType: "generate-preview",
    jobLabel: "Preview Build",
    jobDescription: "Builds video previews.",
    queueName: "preview",
    queueLabel: "Preview Build",
    status: "failed",
    targetType: "video",
    targetId: "video-1",
    targetLabel: "Video 1",
    triggeredBy: "system",
    triggerLabel: "Queued by jobs",
    jobKind: "standard",
    progress: 0,
    attempts: 2,
    statusMessage: null,
    error: "Cannot generate trickplay",
    startedAt: "2026-05-13T11:59:00Z",
    finishedAt: null,
    createdAt: "2026-05-13T11:58:00Z",
    updatedAt: "2026-05-13T12:00:00Z",
    ...overrides,
  };
}
