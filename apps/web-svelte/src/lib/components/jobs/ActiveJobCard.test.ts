import { render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import type { JobRun } from "$lib/jobs/models";
import ActiveJobCard from "./ActiveJobCard.svelte";

describe("ActiveJobCard", () => {
  it("renders the job kind, target detail, and live status message", () => {
    render(ActiveJobCard, {
      props: {
        job: jobRun({
          jobType: "generate-book-page-thumbnail",
          jobLabel: "Book Page Thumbnail",
          jobDescription: "Generates thumbnails for comic book pages.",
          queueName: "book-page-thumbnail",
          queueLabel: "Book Page Thumbnail",
          targetType: "book-page",
          targetId: "page-14",
          targetLabel: "14",
          status: "active",
          progress: 60,
          statusMessage: "Generating thumbnail",
        }),
        nsfwMode: "safe",
        cancellingJobRunId: null,
        onCancelJob: vi.fn(),
      },
    });

    expect(screen.getByText("14")).toBeInTheDocument();
    expect(screen.getByText("Book Page Thumbnail")).toBeInTheDocument();
    expect(screen.getByText("book page page-14")).toBeInTheDocument();
    expect(screen.getByText("Generating thumbnail")).toBeInTheDocument();
    expect(screen.getByText("60%")).toBeInTheDocument();
  });
});

function jobRun(overrides: Partial<JobRun>): JobRun {
  return {
    id: "job-1",
    jobType: "scan-library",
    jobLabel: "Video Scan",
    jobDescription: "Discovers videos in configured library roots.",
    queueName: "library-scan",
    queueLabel: "Library Scan",
    status: "waiting",
    targetType: null,
    targetId: null,
    targetLabel: null,
    triggeredBy: "system",
    triggerLabel: "Queued by jobs",
    jobKind: "standard",
    progress: 0,
    attempts: 0,
    statusMessage: null,
    error: null,
    startedAt: null,
    finishedAt: null,
    createdAt: "2026-05-13T12:00:00Z",
    updatedAt: "2026-05-13T12:00:00Z",
    ...overrides,
  };
}
