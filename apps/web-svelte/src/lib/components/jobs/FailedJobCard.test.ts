import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import type { JobRun } from "$lib/jobs/models";
import FailedJobCard from "./FailedJobCard.svelte";

describe("FailedJobCard", () => {
  it("renders grouped occurrence counts and suppresses the group fingerprint", async () => {
    const onDismiss = vi.fn();

    render(FailedJobCard, {
      props: {
        job: jobRun({ error: "Cannot generate trickplay" }),
        nsfwMode: "safe",
        occurrenceCount: 7,
        fingerprint: "preview:Cannot generate trickplay",
        onDismiss,
      },
    });

    expect(screen.getByText("7 occurrences")).toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: /suppress/i }));

    expect(onDismiss).toHaveBeenCalledWith("preview:Cannot generate trickplay");
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
    finishedAt: "2026-05-13T12:00:00Z",
    createdAt: "2026-05-13T11:58:00Z",
    updatedAt: "2026-05-13T12:00:00Z",
    ...overrides,
  };
}
