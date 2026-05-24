import type { LedStatus, BadgeVariant } from "@prismedia/ui-svelte";
import type { JobRun, QueueSummary } from "./models";
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

export function formatRelativeTime(value: string | null) {
  if (!value) return "Never";

  const diffMs = Date.now() - new Date(value).getTime();
  const diffMinutes = Math.max(0, Math.floor(diffMs / 60_000));

  if (diffMinutes < 1) return "just now";
  if (diffMinutes < 60) return `${diffMinutes}m ago`;

  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) return `${diffHours}h ago`;

  const diffDays = Math.floor(diffHours / 24);
  return `${diffDays}d ago`;
}

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

export function maintenanceJobLogRedacted(job: JobRun, nsfwMode: string) {
  return nsfwMode === "off" && job.queueName === "library-maintenance";
}

export function displayJobHeading(job: JobRun, nsfwMode: string): string {
  if (maintenanceJobLogRedacted(job, nsfwMode)) return "Relocate video generated files";
  return jobHeading(job);
}

export function displayDescribeTrigger(job: JobRun, nsfwMode: string): string {
  if (maintenanceJobLogRedacted(job, nsfwMode)) return "Background file layout task";
  return describeTrigger(job);
}

export function displayJobKind(job: JobRun, nsfwMode: string): string {
  if (maintenanceJobLogRedacted(job, nsfwMode)) return "Library Maintenance";
  return job.jobLabel;
}

export function displayJobDetail(job: JobRun, nsfwMode: string): string {
  if (maintenanceJobLogRedacted(job, nsfwMode)) return "Background file layout task";
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
  if (!value) return "Never";
  const diffMs = Date.now() - new Date(value).getTime();
  const diffMinutes = Math.max(0, Math.floor(diffMs / 60_000));
  if (diffMinutes < 1) return "now";
  if (diffMinutes < 60) return `${diffMinutes}m`;
  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) return `${diffHours}h`;
  return `${Math.floor(diffHours / 24)}d`;
}

export function errorFingerprint(job: Pick<JobRun, "queueName" | "error">): string {
  return `${job.queueName}:${(job.error ?? "").trim().slice(0, 200)}`;
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
