import { describe, expect, it, vi } from "vitest";
import {
  backfillFingerprints,
  cancelJobs,
  createJob,
  fetchWorkerHealth,
  rebuildPreviews,
} from "./jobs";

describe("jobs API", () => {
  it("creates jobs through the generated 202 response", async () => {
    const fetchMock = mockFetch({
      job: {
        id: "job-1",
        type: "scan-library",
        status: "queued",
        progress: 0,
        message: null,
        targetKind: null,
        targetId: null,
        targetLabel: null,
        createdAt: "2026-05-27T00:00:00Z",
        startedAt: null,
        finishedAt: null,
      },
    }, 202);

    const response = await createJob("scan-library");

    expect(fetchMock).toHaveBeenCalledWith("/api/jobs/scan-library", expect.anything());
    expect(response.job.id).toBe("job-1");
  });

  it("normalizes operation counts from generated responses", async () => {
    mockFetch({ cancelled: "2" });

    const response = await cancelJobs("scan-library");

    expect(response.cancelled).toBe(2);
  });

  it("omits the type query parameter for cancel all", async () => {
    const fetchMock = mockFetch({ cancelled: 3 });

    const response = await cancelJobs();

    expect(fetchMock).toHaveBeenCalledWith("/api/jobs", expect.anything());
    expect(response.cancelled).toBe(3);
  });

  it("normalizes maintenance job counts", async () => {
    mockFetch({ enqueued: "7", skipped: "3" });

    const response = await rebuildPreviews();

    expect(response).toEqual({ enqueued: 7, skipped: 3 });
  });

  it("normalizes worker heartbeat staleness", async () => {
    mockFetch({
      status: "offline",
      workerId: null,
      lastSeenAt: null,
      staleAfterSeconds: "45",
    });

    const response = await fetchWorkerHealth();

    expect(response.staleAfterSeconds).toBe(45);
  });

  it("uses the generated backfill endpoint", async () => {
    const fetchMock = mockFetch({ enqueued: 1, skipped: 0 });

    await backfillFingerprints();

    expect(fetchMock).toHaveBeenCalledWith("/api/jobs/backfill-fingerprints", expect.anything());
  });
});

function mockFetch(data: unknown, status = 200) {
  const fetchMock = vi.fn(async () => new Response(JSON.stringify(data), {
    headers: { "Content-Type": "application/json" },
    status,
  }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}
