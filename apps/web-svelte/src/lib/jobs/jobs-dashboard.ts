import type { JobQueueCountDto, JobRun as ApiJobRun } from "$lib/api/generated/model";
import {
  queueDefinitions,
  type JobRun as DashboardJobRun,
  type JobRunGroup,
  type JobsDashboard,
  type JobStatus,
  type QueueName,
  type QueueSummary,
} from "./models";

type JobDefinition = {
  type: string;
  queueName: QueueName;
  label: string;
  description: string;
};

const queueDefinitionByName = new Map(queueDefinitions.map((queue) => [queue.name, queue]));

const _JOB_DEFINITIONS = [
  // Scanning
  {
    type: "scan-library",
    queueName: "library-scan",
    label: "Video Scan",
    description: "Discovers videos in configured library roots.",
  },
  {
    type: "scan-gallery",
    queueName: "gallery-scan",
    label: "Gallery Scan",
    description: "Discovers image galleries in configured library roots.",
  },
  {
    type: "scan-book",
    queueName: "book-scan",
    label: "Book Scan",
    description: "Discovers comic books in configured library roots.",
  },
  {
    type: "scan-audio",
    queueName: "audio-scan",
    label: "Audio Scan",
    description: "Discovers audio tracks in configured library roots.",
  },
  // Probing
  {
    type: "probe-video",
    queueName: "media-probe",
    label: "Video Probe",
    description: "Extracts technical metadata from video files via ffprobe.",
  },
  {
    type: "probe-audio",
    queueName: "audio-probe",
    label: "Audio Probe",
    description: "Extracts technical metadata and embedded tags from audio files.",
  },
  // Fingerprinting
  {
    type: "fingerprint-video",
    queueName: "fingerprint",
    label: "Video Fingerprint",
    description: "Computes MD5 and oshash for videos.",
  },
  {
    type: "fingerprint-image",
    queueName: "image-fingerprint",
    label: "Image Fingerprint",
    description: "Computes MD5 and oshash for images.",
  },
  {
    type: "fingerprint-audio",
    queueName: "audio-fingerprint",
    label: "Audio Fingerprint",
    description: "Computes MD5 and oshash for audio tracks.",
  },
  // Preview / asset generation
  {
    type: "generate-preview",
    queueName: "preview",
    label: "Video Preview",
    description: "Builds video thumbnails, preview clips, and trickplay sprites.",
  },
  {
    type: "generate-image-thumbnail",
    queueName: "image-thumbnail",
    label: "Image Thumbnail",
    description: "Generates thumbnails and lightweight previews for images.",
  },
  {
    type: "generate-book-page-thumbnail",
    queueName: "book-page-thumbnail",
    label: "Book Page Thumbnail",
    description: "Generates thumbnails for comic book pages.",
  },
  {
    type: "generate-audio-waveform",
    queueName: "audio-waveform",
    label: "Audio Waveform",
    description: "Generates waveform peak data for audio playback visualization.",
  },
  {
    type: "extract-subtitles",
    queueName: "extract-subtitles",
    label: "Subtitle Extraction",
    description: "Extracts embedded subtitle tracks from video files as WebVTT.",
  },
  // Metadata / collections / maintenance
  {
    type: "import-metadata",
    queueName: "metadata-import",
    label: "Metadata Import",
    description: "Coordinates provider imports and applies metadata to entities.",
  },
  {
    type: "auto-identify",
    queueName: "metadata-import",
    label: "Auto Identify",
    description: "Identifies newly scanned media through enabled plugins and applies confident matches.",
  },
  {
    type: "bulk-identify",
    queueName: "metadata-import",
    label: "Bulk Identify",
    description: "Searches a provider for multiple entities and queues results for review.",
  },
  {
    type: "refresh-collection",
    queueName: "collection-refresh",
    label: "Collection Refresh",
    description: "Re-evaluates dynamic collection rules and updates membership.",
  },
  {
    type: "monitored-search",
    queueName: "monitored-search",
    label: "Monitored Search",
    description: "Re-searches monitored items and syncs followed authors/artists for new works.",
  },
  {
    type: "library-maintenance",
    queueName: "library-maintenance",
    label: "Library Maintenance",
    description: "Moves video-derived assets between cache and media-adjacent storage.",
  },
  // Utility
  {
    type: "noop",
    queueName: "library-maintenance",
    label: "No-op Worker Check",
    description: "Queues a tiny worker health check job.",
  },
] satisfies readonly JobDefinition[];

const jobDefinitionByType = new Map(
  _JOB_DEFINITIONS.map((definition) => [definition.type, definition]),
);

export function jobTypeForQueue(queueName: string): string | null {
  return (
    _JOB_DEFINITIONS.find((definition) => definition.queueName === queueName)?.type ??
    null
  );
}

export function jobTypesForQueue(queueName: string): string[] {
  return _JOB_DEFINITIONS.filter((definition) => definition.queueName === queueName).map(
    (definition) => definition.type,
  );
}

function definitionForJob(type: string): JobDefinition {
  return (
    jobDefinitionByType.get(type) ?? {
      type,
      queueName: "library-maintenance",
      label: type,
      description: "Background job managed by the worker.",
    }
  );
}

function queueSummaryBase(definition: JobDefinition): QueueSummary {
  const queueDefinition = queueDefinitionByName.get(definition.queueName);
  return {
    name: definition.queueName,
    label: queueDefinition?.label || definition.label || definition.queueName,
    description: definition.description || queueDefinition?.description || "",
    status: "idle",
    concurrency: queueDefinition?.concurrency ?? 1,
    active: 0,
    waiting: 0,
    delayed: 0,
    backlog: 0,
    completed: 0,
    failed: 0,
  };
}

export function mapJobStatus(status: string): JobStatus {
  switch (status) {
    case "running":
      return "active";
    case "completed":
      return "completed";
    case "failed":
      return "failed";
    case "cancelled":
      return "dismissed";
    case "queued":
    default:
      return "waiting";
  }
}

function normalizeProgress(progress: ApiJobRun["progress"]): number {
  const value = Number(progress);
  if (!Number.isFinite(value)) return 0;
  return Math.min(100, Math.max(0, Math.round(value)));
}

export function mapJobRun(job: ApiJobRun): DashboardJobRun {
  const definition = definitionForJob(job.type);
  const queueDefinition = queueDefinitionByName.get(definition.queueName);
  const status = mapJobStatus(job.status);

  return {
    id: job.id,
    jobType: job.type,
    jobLabel: definition.label,
    jobDescription: definition.description,
    queueName: definition.queueName,
    queueLabel: queueDefinition?.label ?? definition.label,
    status,
    targetType: job.targetKind ?? job.type,
    targetId: job.targetId ?? null,
    targetLabel: job.targetLabel ?? null,
    triggeredBy: "system",
    triggerLabel: "Queued by jobs",
    jobKind: "standard",
    progress: normalizeProgress(job.progress),
    attempts: 0,
    statusMessage: job.message,
    error: status === "failed" ? job.message : null,
    startedAt: job.startedAt,
    finishedAt: job.finishedAt,
    createdAt: job.createdAt,
    updatedAt: job.finishedAt ?? job.startedAt ?? job.createdAt,
  };
}

export function groupJobRunsByKind(jobs: readonly DashboardJobRun[]): JobRunGroup[] {
  const groups = new Map<string, JobRunGroup>();

  for (const job of jobs) {
    let group = groups.get(job.jobType);
    if (!group) {
      group = {
        key: job.jobType,
        jobType: job.jobType,
        jobLabel: job.jobLabel,
        jobDescription: job.jobDescription,
        queueName: job.queueName,
        queueLabel: job.queueLabel,
        jobs: [],
        activeCount: 0,
        waitingCount: 0,
        totalCount: 0,
      };
      groups.set(job.jobType, group);
    }

    group.jobs.push(job);
    group.totalCount += 1;
    if (job.status === "active") group.activeCount += 1;
    if (job.status === "waiting" || job.status === "delayed") group.waitingCount += 1;
  }

  return [...groups.values()].sort(
    (a, b) =>
      b.activeCount - a.activeCount ||
      b.waitingCount - a.waitingCount ||
      a.jobLabel.localeCompare(b.jobLabel),
  );
}

export interface ScheduleInfo {
  enabled: boolean;
  intervalMinutes: number;
}

export function buildJobsDashboard(
  jobs: readonly ApiJobRun[],
  schedule?: ScheduleInfo,
  counts?: readonly JobQueueCountDto[],
): JobsDashboard {
  const mappedJobs = jobs.map(mapJobRun);
  const summaries = new Map<QueueName, QueueSummary>();

  for (const definition of _JOB_DEFINITIONS) {
    if (!summaries.has(definition.queueName)) {
      summaries.set(definition.queueName, queueSummaryBase(definition));
    }
  }

  if (counts && counts.length > 0) {
    for (const { type, status, count } of counts) {
      const def = jobDefinitionByType.get(type);
      if (!def) continue;
      const summary = summaries.get(def.queueName);
      if (!summary) continue;
      const countValue = Number(count);
      if (!Number.isFinite(countValue)) continue;

      const mapped = mapJobStatus(status);
      if (mapped === "active") summary.active += countValue;
      else if (mapped === "waiting") summary.waiting += countValue;
      else if (mapped === "completed") summary.completed += countValue;
      else if (mapped === "failed") summary.failed += countValue;
    }
  } else {
    for (const job of mappedJobs) {
      const summary = summaries.get(job.queueName);
      if (!summary) continue;

      if (job.status === "active") summary.active += 1;
      if (job.status === "waiting") summary.waiting += 1;
      if (job.status === "delayed") summary.delayed += 1;
      if (job.status === "completed") summary.completed += 1;
      if (job.status === "failed") summary.failed += 1;
    }
  }

  for (const summary of summaries.values()) {
    summary.backlog = summary.waiting + summary.delayed;
    summary.status = summary.failed > 0 ? "warning" : summary.active + summary.backlog > 0 ? "active" : "idle";
  }

  const activeJobs = mappedJobs.filter((job) => job.status === "active");
  const failedJobs = mappedJobs.filter((job) => job.status === "failed");
  const completedJobs = mappedJobs
    .filter((job) => job.status === "completed" || job.status === "dismissed")
    .slice(0, 40);
  const lastScanAt =
    mappedJobs.find((job) => job.queueName === "library-scan" && job.status === "completed")
      ?.finishedAt ?? null;

  return {
    queues: [...summaries.values()],
    activeJobs,
    failedJobs,
    completedJobs,
    recentJobs: mappedJobs,
    lastScanAt,
    schedule: {
      enabled: schedule?.enabled ?? false,
      intervalMinutes: schedule?.intervalMinutes ?? 0,
    },
  };
}
