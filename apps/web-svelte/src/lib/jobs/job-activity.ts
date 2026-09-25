import type { JobActivityBucket, JobRun } from "$lib/api/generated/model";

/** One hour of background work. */
export interface ActivityBucket {
  /** Epoch milliseconds at the start of the hour. */
  start: number;
  total: number;
  failed: number;
  running: number;
}

export const ACTIVITY_WINDOW_HOURS = 24;
const HOUR_MS = 3_600_000;

/** The moment a run belongs to on a timeline: when it finished, else started, else was queued. */
export function jobRunMoment(run: Pick<JobRun, "createdAt" | "startedAt" | "finishedAt">): string {
  return run.finishedAt ?? run.startedAt ?? run.createdAt;
}

/**
 * Lays the server's per-hour buckets for one job type into the last 24 hourly slots, oldest first.
 * The server already counts running work in the current hour, so a long scan reads as live.
 */
export function fillActivityBuckets(buckets: readonly JobActivityBucket[], now: number): ActivityBucket[] {
  const currentHour = Math.floor(now / HOUR_MS) * HOUR_MS;
  const firstHour = currentHour - (ACTIVITY_WINDOW_HOURS - 1) * HOUR_MS;
  const slots: ActivityBucket[] = Array.from({ length: ACTIVITY_WINDOW_HOURS }, (_, index) => ({
    start: firstHour + index * HOUR_MS,
    total: 0,
    failed: 0,
    running: 0,
  }));

  for (const bucket of buckets) {
    const start = Date.parse(bucket.start);
    if (!Number.isFinite(start) || start < firstHour) continue;
    const slot = slots[Math.min(ACTIVITY_WINDOW_HOURS - 1, Math.floor((start - firstHour) / HOUR_MS))];
    if (!slot) continue;
    slot.total += Number(bucket.total) || 0;
    slot.failed += Number(bucket.failed) || 0;
    slot.running += Number(bucket.running) || 0;
  }
  return slots;
}

/** Runs that landed inside the 24-hour window. */
export function activityTotal(buckets: readonly ActivityBucket[]): number {
  return buckets.reduce((sum, bucket) => sum + bucket.total, 0);
}
