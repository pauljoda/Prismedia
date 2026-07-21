import { JobRunStatus, type JobQueueCountDto, type JobRun as ApiJobRun } from "$lib/api/generated/model";
import { JOB_TYPE, type JobTypeCode } from "$lib/api/generated/codes";
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
  type: JobTypeCode;
  queueName: QueueName;
  label: string;
  description: string;
};

/** A definition resolved for an arbitrary runtime type string (possibly newer than this build). */
type ResolvedJobDefinition = Omit<JobDefinition, "type"> & { type: string };

const queueDefinitionByName = new Map(queueDefinitions.map((queue) => [queue.name, queue]));

const _JOB_DEFINITIONS = [
  // Scanning
  {
    type: JOB_TYPE.scanLibrary,
    queueName: "library-scan",
    label: "Video Scan",
    description: "Discovers videos in configured library roots.",
  },
  {
    type: JOB_TYPE.scanGallery,
    queueName: "gallery-scan",
    label: "Gallery Scan",
    description: "Discovers image galleries in configured library roots.",
  },
  {
    type: JOB_TYPE.scanBook,
    queueName: "book-scan",
    label: "Book Scan",
    description: "Discovers comic books in configured library roots.",
  },
  {
    type: JOB_TYPE.scanAudio,
    queueName: "audio-scan",
    label: "Audio Scan",
    description: "Discovers audio tracks in configured library roots.",
  },
  // Probing
  {
    type: JOB_TYPE.probeVideo,
    queueName: "media-probe",
    label: "Video Probe",
    description: "Extracts technical metadata from video files via ffprobe.",
  },
  {
    type: JOB_TYPE.probeAudio,
    queueName: "audio-probe",
    label: "Audio Probe",
    description: "Extracts technical metadata and embedded tags from audio files.",
  },
  // Fingerprinting
  {
    type: JOB_TYPE.fingerprintVideo,
    queueName: "fingerprint",
    label: "Video Fingerprint",
    description: "Computes MD5 and oshash for videos.",
  },
  {
    type: JOB_TYPE.fingerprintImage,
    queueName: "image-fingerprint",
    label: "Image Fingerprint",
    description: "Computes MD5 and oshash for images.",
  },
  {
    type: JOB_TYPE.fingerprintAudio,
    queueName: "audio-fingerprint",
    label: "Audio Fingerprint",
    description: "Computes MD5 and oshash for audio tracks.",
  },
  // Preview / asset generation
  {
    type: JOB_TYPE.generatePreview,
    queueName: "preview",
    label: "Video Preview",
    description: "Builds video thumbnails, preview clips, and trickplay sprites.",
  },
  {
    type: JOB_TYPE.generateImageThumbnail,
    queueName: "image-thumbnail",
    label: "Image Thumbnail",
    description: "Generates thumbnails and lightweight previews for images.",
  },
  {
    type: JOB_TYPE.generateGridThumbnail,
    queueName: "image-thumbnail",
    label: "Grid Thumbnail",
    description: "Derives the small grid-card variants from an entity's cover.",
  },
  {
    type: JOB_TYPE.gridThumbnailSweep,
    queueName: "image-thumbnail",
    label: "Grid Thumbnail Sweep",
    description: "Backfills missing grid-card thumbnail variants across the library.",
  },
  {
    type: JOB_TYPE.generateBookPageThumbnail,
    queueName: "book-page-thumbnail",
    label: "Book Page Thumbnail",
    description: "Generates thumbnails for comic book pages.",
  },
  {
    type: JOB_TYPE.generateBookCoverThumbnail,
    queueName: "book-page-thumbnail",
    label: "Book Cover Thumbnail",
    description: "Extracts and generates cover thumbnails for books and comics.",
  },
  {
    type: JOB_TYPE.generateAudioWaveform,
    queueName: "audio-waveform",
    label: "Audio Waveform",
    description: "Generates waveform peak data for audio playback visualization.",
  },
  {
    type: JOB_TYPE.extractSubtitles,
    queueName: "extract-subtitles",
    label: "Subtitle Extraction",
    description: "Extracts embedded subtitle tracks from video files as WebVTT.",
  },
  {
    type: JOB_TYPE.acquireSubtitles,
    queueName: "acquire-subtitles",
    label: "Subtitle Acquisition",
    description: "Finds and imports trusted provider subtitles for missing preferred languages.",
  },
  // Metadata / identify
  {
    type: JOB_TYPE.importMetadata,
    queueName: "metadata-import",
    label: "Metadata Import",
    description: "Coordinates provider imports and applies metadata to entities.",
  },
  {
    type: JOB_TYPE.autoIdentify,
    queueName: "metadata-import",
    label: "Auto Identify",
    description: "Identifies newly scanned media through enabled plugins and applies confident matches.",
  },
  {
    type: JOB_TYPE.bulkIdentify,
    queueName: "metadata-import",
    label: "Bulk Identify",
    description: "Searches a provider for multiple entities and queues results for review.",
  },
  {
    type: JOB_TYPE.identifySearch,
    queueName: "metadata-import",
    label: "Identify Search",
    description: "Searches metadata providers for identify candidates.",
  },
  {
    type: JOB_TYPE.identifyCascade,
    queueName: "metadata-import",
    label: "Identify Cascade",
    description: "Applies a confirmed identification to related child entities.",
  },
  {
    type: JOB_TYPE.refreshEntity,
    queueName: "metadata-import",
    label: "Entity Refresh",
    description: "Re-queues probing, artwork, and metadata work for an entity tree.",
  },
  // Collections / maintenance
  {
    type: JOB_TYPE.refreshCollection,
    queueName: "collection-refresh",
    label: "Collection Refresh",
    description: "Re-evaluates dynamic collection rules and updates membership.",
  },
  {
    type: JOB_TYPE.libraryMaintenance,
    queueName: "library-maintenance",
    label: "Library Maintenance",
    description: "Validates generated assets and cleans up orphaned cache files.",
  },
  {
    type: JOB_TYPE.recycleBinCleanup,
    queueName: "library-maintenance",
    label: "Recycle Bin Cleanup",
    description: "Purges recycle-bin entries older than the retention window.",
  },
  {
    type: JOB_TYPE.databaseBackup,
    queueName: "database-backup",
    label: "Database Backup",
    description: "Creates a retained automatic database backup.",
  },
  // Acquisition
  {
    type: JOB_TYPE.acquisitionSearch,
    queueName: "acquisition",
    label: "Acquisition Search",
    description: "Searches configured indexers for releases of a wanted item.",
  },
  {
    type: JOB_TYPE.acquisitionMonitor,
    queueName: "acquisition",
    label: "Acquisition Monitor",
    description: "Tracks in-flight downloads and hands completed ones to import.",
  },
  {
    type: JOB_TYPE.acquisitionImport,
    queueName: "acquisition",
    label: "Acquisition Import",
    description: "Imports completed downloads into the library.",
  },
  {
    type: JOB_TYPE.acquisitionFailedHandle,
    queueName: "acquisition",
    label: "Failed Download Handling",
    description: "Blocklists a failed release and searches for a replacement.",
  },
  {
    type: JOB_TYPE.acquisitionUpgradeReplace,
    queueName: "acquisition",
    label: "Upgrade Replace",
    description: "Swaps an owned file for a higher-quality grabbed release.",
  },
  {
    type: JOB_TYPE.acquisitionEnrich,
    queueName: "acquisition",
    label: "Acquisition Enrich",
    description: "Fills provider metadata and artwork for request-created entities.",
  },
  {
    type: JOB_TYPE.requestAcquisitionFanout,
    queueName: "acquisition",
    label: "Request Fan-out",
    description: "Starts acquisition searches for children committed by a container request.",
  },
  {
    type: JOB_TYPE.monitoredSearch,
    queueName: "monitored-search",
    label: "Monitored Search",
    description: "Re-searches monitored items and syncs followed authors/artists for new works.",
  },
  // Utility
  {
    type: JOB_TYPE.noop,
    queueName: "background",
    label: "No-op Worker Check",
    description: "Queues a tiny worker health check job.",
  },
] satisfies readonly JobDefinition[];

// Compile-time parity with the backend's JobType closed set: adding a JobType (and regenerating
// codes.ts) without cataloguing it here fails the build instead of silently falling back.
type CoveredJobType = (typeof _JOB_DEFINITIONS)[number]["type"];
type AssertAllJobTypesCovered<Missing extends never> = Missing;
type _JobCatalogIsComplete = AssertAllJobTypesCovered<Exclude<JobTypeCode, CoveredJobType>>;

const jobDefinitionByType = new Map<string, JobDefinition>(
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

function definitionForJob(type: string): ResolvedJobDefinition {
  return (
    jobDefinitionByType.get(type) ?? {
      type,
      queueName: "background",
      label: type,
      description: "Background job managed by the worker.",
    }
  );
}

function queueSummaryBase(definition: ResolvedJobDefinition): QueueSummary {
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
    case JobRunStatus.running:
      return "active";
    case JobRunStatus.completed:
      return "completed";
    case JobRunStatus.failed:
      return "failed";
    case JobRunStatus.cancelled:
      return "dismissed";
    case JobRunStatus.queued:
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
