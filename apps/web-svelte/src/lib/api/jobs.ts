import {
  backfillFingerprints as backfillFingerprintsRequest,
  cancelJobRun as cancelJobRunRequest,
  cancelJobs as cancelJobsRequest,
  clearJobFailures as clearJobFailuresRequest,
  createJob as createJobRequest,
  getWorkerHealth,
  listJobs,
  rebuildPreviews as rebuildPreviewsRequest,
} from "$lib/api/generated/prismedia";
import type {
  BulkJobResponse as GeneratedBulkJobResponse,
  JobCancelResponse as GeneratedJobCancelResponse,
  JobCreateResponse,
  JobFailureClearResponse as GeneratedJobFailureClearResponse,
  JobListResponse,
  JobRun as GeneratedJobRun,
  WorkerHealthResponse as GeneratedWorkerHealthResponse,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";

export type JobRun = GeneratedJobRun & {
  targetKind?: string | null;
  targetId?: string | null;
  targetLabel?: string | null;
};
export type { JobCreateResponse, JobListResponse };

export interface JobCancelResponse {
  cancelled: number;
}

export interface JobFailureClearResponse {
  cleared: number;
}

export interface BulkJobResponse {
  enqueued: number;
  skipped: number;
}

export interface WorkerHealthResponse {
  status: "online" | "offline" | string;
  workerId: string | null;
  lastSeenAt: string | null;
  staleAfterSeconds: number;
}

export async function fetchJobs(options?: RequestOptions): Promise<JobListResponse> {
  return unwrapGenerated(
    await listJobs(undefined, requestInit(options)),
    "Failed to load jobs",
  );
}

export async function fetchWorkerHealth(
  options?: RequestOptions,
): Promise<WorkerHealthResponse> {
  return normalizeWorkerHealth(
    unwrapGenerated(
      await getWorkerHealth(requestInit(options)),
      "Failed to load worker health",
    ),
  );
}

export async function createJob(
  type: string,
  options?: RequestOptions,
): Promise<JobCreateResponse> {
  return unwrapGenerated(
    await createJobRequest(type, requestInit(options)),
    `Failed to queue ${type}`,
    [202],
  );
}

export async function cancelJobs(
  type?: string | null,
  options?: RequestOptions,
): Promise<JobCancelResponse> {
  return normalizeJobCancel(
    unwrapGenerated(
      await cancelJobsRequest(type ? { type } : undefined, requestInit(options)),
      "Failed to cancel jobs",
    ),
  );
}

export async function cancelJobRun(
  id: string,
  options?: RequestOptions,
): Promise<JobCancelResponse> {
  return normalizeJobCancel(
    unwrapGenerated(
      await cancelJobRunRequest(id, requestInit(options)),
      "Failed to cancel job",
    ),
  );
}

export async function clearJobFailures(
  type?: string | null,
  options?: RequestOptions,
): Promise<JobFailureClearResponse> {
  return normalizeJobFailureClear(
    unwrapGenerated(
      await clearJobFailuresRequest(type ? { type } : undefined, requestInit(options)),
      "Failed to clear job failures",
    ),
  );
}

export async function rebuildPreviews(options?: RequestOptions): Promise<BulkJobResponse> {
  return normalizeBulkJob(
    unwrapGenerated(
      await rebuildPreviewsRequest(requestInit(options)),
      "Failed to queue preview rebuild",
    ),
  );
}

export async function backfillFingerprints(
  options?: RequestOptions,
): Promise<BulkJobResponse> {
  return normalizeBulkJob(
    unwrapGenerated(
      await backfillFingerprintsRequest(requestInit(options)),
      "Failed to queue fingerprint backfill",
    ),
  );
}

function normalizeJobCancel(response: GeneratedJobCancelResponse): JobCancelResponse {
  return {
    cancelled: normalizeNumber(response.cancelled),
  };
}

function normalizeJobFailureClear(
  response: GeneratedJobFailureClearResponse,
): JobFailureClearResponse {
  return {
    cleared: normalizeNumber(response.cleared),
  };
}

function normalizeBulkJob(response: GeneratedBulkJobResponse): BulkJobResponse {
  return {
    enqueued: normalizeNumber(response.enqueued),
    skipped: normalizeNumber(response.skipped),
  };
}

function normalizeWorkerHealth(
  response: GeneratedWorkerHealthResponse,
): WorkerHealthResponse {
  return {
    ...response,
    staleAfterSeconds: normalizeNumber(response.staleAfterSeconds),
  };
}

function normalizeNumber(value: number | string): number {
  return Number(value);
}
