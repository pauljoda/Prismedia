import type { LedStatus, BadgeVariant } from "@prismedia/ui-svelte";
import type { FailedJobGroup, JobRun, QueueSummary } from "./models";
import { formatRelativeTime } from "$lib/utils/format";
import {
  Cpu,
  BookOpen,
  DatabaseZap,
  FileSearch,
  Fingerprint,
  FolderSearch,
  Image,
} from "@lucide/svelte";
import type { LucideIcon } from "@lucide/svelte";

export type QueueIcon = LucideIcon;

export const queueIcons: Record<string, QueueIcon> = {
  "library-scan": FolderSearch,
  "media-probe": FileSearch,
  fingerprint: Fingerprint,
  preview: Image,
  "metadata-import": DatabaseZap,
  "gallery-scan": FolderSearch,
  "book-scan": BookOpen,
  "image-thumbnail": Image,
  "book-page-thumbnail": BookOpen,
  "image-fingerprint": Fingerprint,
};

export function getQueueIcon(queueName: string): QueueIcon {
  return queueIcons[queueName] ?? Cpu;
}

export function formatStamp(value: string | null) {
  if (!value) return "Never";
  return new Date(value).toLocaleString();
}

export { formatRelativeTime } from "$lib/utils/format";

export function formatElapsed(job: JobRun): string {
  const anchor = job.startedAt ?? job.createdAt;
  const deltaSeconds = Math.max(
    0,
    Math.round((Date.now() - new Date(anchor).getTime()) / 1000),
  );
  const minutes = Math.floor(deltaSeconds / 60);
  const seconds = deltaSeconds % 60;

  if (job.status === "waiting" || job.status === "delayed") {
    return `queued ${minutes}m ${String(seconds).padStart(2, "0")}s`;
  }

  return `${minutes}m ${String(seconds).padStart(2, "0")}s`;
}

export function ledForQueue(status: QueueSummary["status"]): LedStatus {
  if (status === "active") return "phosphor";
  if (status === "warning") return "warning";
  return "idle";
}

export function isForceRebuildJob(job: JobRun) {
  return job.jobKind === "force-rebuild";
}

export function toneForJob(job: JobRun): LedStatus {
  if (job.status === "failed") return "error";
  if (isForceRebuildJob(job)) return "error";
  if (job.status === "waiting" || job.status === "delayed") return "warning";
  return "phosphor";
}

export function describeTrigger(job: JobRun): string {
  if (job.triggerLabel) return job.triggerLabel;

  switch (job.triggeredBy) {
    case "manual":
      return "Started manually";
    case "schedule":
      return "Started by recurring scan schedule";
    case "library-scan":
      return "Queued during library scan";
    case "gallery-scan":
      return "Queued during gallery scan";
    case "book-scan":
      return "Queued during book scan";
    case "system":
      return "Queued by the system";
    default:
      return "Trigger not recorded";
  }
}

export function statusLabel(status: JobRun["status"]): string {
  if (status === "waiting") return "queued";
  if (status === "delayed") return "delayed";
  if (status === "dismissed") return "cleared";
  return status;
}

export function jobHeading(job: JobRun): string {
  return job.targetLabel ?? job.jobLabel;
}

export function formatTargetDetail(job: JobRun): string | null {
  const targetType = job.targetType?.trim();
  const targetId = job.targetId?.trim();
  if (!targetType && !targetId) return null;

  const label = targetType ? targetType.replaceAll("-", " ") : "target";
  return targetId ? `${label} ${targetId}` : label;
}

/**
 * Library-maintenance jobs can carry NSFW file names in their target labels and
 * messages, so their details are hidden while the viewer's NSFW mode is off. Only
 * the free-text fields are masked — the job keeps its real kind label.
 */
export function maintenanceJobLogRedacted(job: JobRun, nsfwMode: string) {
  return nsfwMode === "off" && job.queueName === "library-maintenance";
}

export function displayJobHeading(job: JobRun, nsfwMode: string): string {
  if (maintenanceJobLogRedacted(job, nsfwMode)) return "Library maintenance";
  return jobHeading(job);
}

export function displayDescribeTrigger(job: JobRun, nsfwMode: string): string {
  if (maintenanceJobLogRedacted(job, nsfwMode)) return "Background maintenance task";
  return describeTrigger(job);
}

export function displayJobKind(job: JobRun, nsfwMode: string): string {
  if (maintenanceJobLogRedacted(job, nsfwMode)) return "Library Maintenance";
  return job.jobLabel;
}

export function displayJobDetail(job: JobRun, nsfwMode: string): string {
  if (maintenanceJobLogRedacted(job, nsfwMode)) return "Background maintenance task";
  return job.statusMessage?.trim() || describeTrigger(job);
}

export function jobBadgeVariant(job: JobRun): BadgeVariant {
  return isForceRebuildJob(job) ? "error" : "accent";
}

export function formatDuration(job: JobRun): string {
  if (!job.startedAt || !job.finishedAt) return "–";
  const ms = new Date(job.finishedAt).getTime() - new Date(job.startedAt).getTime();
  if (ms < 0) return "–";
  const totalSeconds = Math.floor(ms / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (minutes === 0) return `${seconds}s`;
  return `${minutes}m ${String(seconds).padStart(2, "0")}s`;
}

export function formatRelativeTimeShort(value: string | null): string {
  return formatRelativeTime(value, true);
}

export function errorFingerprint(job: Pick<JobRun, "queueName" | "error">): string {
  return `${job.queueName}:${(job.error ?? "").trim().slice(0, 200)}`;
}

function failedAt(job: Pick<JobRun, "finishedAt" | "updatedAt">): string | null {
  return job.finishedAt ?? job.updatedAt ?? null;
}

function failedAtTime(job: Pick<JobRun, "finishedAt" | "updatedAt">): number {
  const value = failedAt(job);
  if (!value) return 0;
  const time = new Date(value).getTime();
  return Number.isFinite(time) ? time : 0;
}

export function groupFailedJobs(jobs: JobRun[]): FailedJobGroup[] {
  const groups = new Map<string, FailedJobGroup>();

  for (const job of jobs) {
    const fingerprint = errorFingerprint(job);
    const existing = groups.get(fingerprint);
    if (!existing) {
      const failed = failedAt(job);
      groups.set(fingerprint, {
        fingerprint,
        representative: job,
        jobs: [job],
        count: 1,
        firstFailedAt: failed,
        lastFailedAt: failed,
      });
      continue;
    }

    existing.jobs.push(job);
    existing.count += 1;

    if (failedAtTime(job) > failedAtTime(existing.representative)) {
      existing.representative = job;
    }

    const currentFailedAt = failedAt(job);
    if (
      currentFailedAt &&
      (!existing.firstFailedAt ||
        new Date(currentFailedAt).getTime() < new Date(existing.firstFailedAt).getTime())
    ) {
      existing.firstFailedAt = currentFailedAt;
    }
    if (
      currentFailedAt &&
      (!existing.lastFailedAt ||
        new Date(currentFailedAt).getTime() > new Date(existing.lastFailedAt).getTime())
    ) {
      existing.lastFailedAt = currentFailedAt;
    }
  }

  return [...groups.values()].sort(
    (a, b) => failedAtTime(b.representative) - failedAtTime(a.representative),
  );
}

export function describeRunResult(
  queueName: string,
  enqueued: number,
  skipped: number,
): string {
  if (queueName === "library-maintenance" && enqueued === 1) {
    return "Cleaning up files.";
  }
  if (queueName === "library-maintenance" && enqueued === 0 && skipped > 0) {
    return "File cleanup is already in progress.";
  }

  if (queueName === "library-scan" && enqueued === 0 && skipped === 0) {
    return "Stale video references cleared. Add a watched folder to scan new files.";
  }
  if (queueName === "library-scan" && enqueued === 0 && skipped > 0) {
    return "Stale references cleared; every video scan is already queued or running.";
  }

  const parts = [
    `Queued ${enqueued} ${queueName} job${enqueued === 1 ? "" : "s"}`,
  ];

  if (skipped > 0) {
    parts.push(`skipped ${skipped} already pending`);
  }

  return `${parts.join(", ")}.`;
}
