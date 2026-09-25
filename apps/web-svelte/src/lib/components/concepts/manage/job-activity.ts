import { JOB_RUN_STATUS } from "$lib/api/generated/codes";
import type { JobRun } from "$lib/api/generated/model";

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
 * Sorts runs into the last 24 hourly buckets, oldest first. Running work always lands in the current
 * hour, so a long scan reads as live rather than as the hour it started in.
 */
export function bucketJobActivity(runs: readonly JobRun[], now: number): ActivityBucket[] {
  const currentHour = Math.floor(now / HOUR_MS) * HOUR_MS;
  const firstHour = currentHour - (ACTIVITY_WINDOW_HOURS - 1) * HOUR_MS;
  const buckets: ActivityBucket[] = Array.from({ length: ACTIVITY_WINDOW_HOURS }, (_, index) => ({
    start: firstHour + index * HOUR_MS,
    total: 0,
    failed: 0,
    running: 0,
  }));

  for (const run of runs) {
    const running = run.status === JOB_RUN_STATUS.running;
    const moment = running ? now : new Date(jobRunMoment(run)).getTime();
    if (!Number.isFinite(moment) || moment < firstHour) continue;
    const bucket = buckets[Math.min(ACTIVITY_WINDOW_HOURS - 1, Math.floor((moment - firstHour) / HOUR_MS))];
    if (!bucket) continue;
    bucket.total += 1;
    if (run.status === JOB_RUN_STATUS.failed) bucket.failed += 1;
    if (running) bucket.running += 1;
  }
  return buckets;
}

/** Runs that landed inside the 24-hour window. */
export function activityTotal(buckets: readonly ActivityBucket[]): number {
  return buckets.reduce((sum, bucket) => sum + bucket.total, 0);
}
