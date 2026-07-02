export const queueDefinitions = [
  {
    name: "library-scan",
    label: "Library Scan",
    description: "Discovers videos (series, seasons, episodes, and movies) in configured media roots",
    concurrency: 1,
  },
  {
    name: "media-probe",
    label: "Media Probe",
    description: "Extracts technical metadata using ffprobe",
    concurrency: 1,
  },
  {
    name: "fingerprint",
    label: "Fingerprint",
    description: "Generates oshash and optional MD5 fingerprints for videos",
    concurrency: 1,
  },
  {
    name: "preview",
    label: "Preview Build",
    description: "Builds video thumbnails, preview clips, and trickplay sprites",
    concurrency: 1,
  },
  {
    name: "metadata-import",
    label: "Metadata Import",
    description: "Coordinates provider imports and metadata application",
    concurrency: 1,
  },
  {
    name: "gallery-scan",
    label: "Gallery Scan",
    description: "Discovers image galleries in configured media roots",
    concurrency: 1,
  },
  {
    name: "book-scan",
    label: "Book Scan",
    description: "Discovers comic books in configured media roots",
    concurrency: 1,
  },
  {
    name: "book-page-thumbnail",
    label: "Book Page Thumbnail",
    description: "Generates thumbnails for comic book pages",
    concurrency: 1,
  },
  {
    name: "image-thumbnail",
    label: "Image Thumbnail",
    description: "Generates thumbnails and lightweight previews for images",
    concurrency: 1,
  },
  {
    name: "image-fingerprint",
    label: "Image Fingerprint",
    description: "Computes oshash and optional MD5 fingerprints for images",
    concurrency: 1,
  },
  {
    name: "audio-scan",
    label: "Audio Scan",
    description: "Discovers audio tracks in configured media roots",
    concurrency: 1,
  },
  {
    name: "audio-probe",
    label: "Audio Probe",
    description: "Extracts technical metadata and embedded tags from audio files",
    concurrency: 1,
  },
  {
    name: "audio-fingerprint",
    label: "Audio Fingerprint",
    description: "Computes oshash and optional MD5 fingerprints for audio tracks",
    concurrency: 1,
  },
  {
    name: "audio-waveform",
    label: "Audio Waveform",
    description: "Generates waveform peaks data for audio playback visualization",
    concurrency: 1,
  },
  {
    name: "library-maintenance",
    label: "Library maintenance",
    description: "Moves video-derived assets between cache and media-adjacent storage",
    concurrency: 1,
  },
  {
    name: "extract-subtitles",
    label: "Extract Subtitles",
    description: "Extracts embedded subtitle tracks from video files as WebVTT",
    concurrency: 1,
  },
  {
    name: "collection-refresh",
    label: "Collection Refresh",
    description: "Re-evaluates dynamic collection rules and updates membership",
    concurrency: 1,
  },
  {
    name: "monitored-search",
    label: "Monitored Search",
    description: "Re-searches monitored items and syncs followed authors/artists for new works",
    concurrency: 1,
  },
  {
    name: "plugin-batch-identify",
    label: "Plugin Batch Identify",
    description: "Batch metadata identification via Prismedia plugins",
    concurrency: 1,
  },
] as const;

export type QueueName = (typeof queueDefinitions)[number]["name"];

export type JobStatus =
  | "waiting"
  | "active"
  | "completed"
  | "failed"
  | "dismissed"
  | "delayed"
  | "paused";

export type JobTriggerKind =
  | "manual"
  | "schedule"
  | "library-scan"
  | "gallery-scan"
  | "book-scan"
  | "audio-scan"
  | "system";

export type JobKind = "standard" | "force-rebuild";

export interface QueueSummary {
  name: QueueName;
  label: string;
  description: string;
  status: "idle" | "active" | "warning";
  concurrency: number;
  active: number;
  waiting: number;
  delayed: number;
  backlog: number;
  completed: number;
  failed: number;
}

export interface JobRun {
  id: string;
  jobType: string;
  jobLabel: string;
  jobDescription: string;
  queueName: QueueName;
  queueLabel: string;
  status: JobStatus;
  targetType: string | null;
  targetId: string | null;
  targetLabel: string | null;
  triggeredBy: JobTriggerKind | null;
  triggerLabel: string | null;
  jobKind: JobKind | null;
  progress: number;
  attempts: number;
  statusMessage: string | null;
  error: string | null;
  startedAt: string | null;
  finishedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface JobRunGroup {
  key: string;
  jobType: string;
  jobLabel: string;
  jobDescription: string;
  queueName: QueueName;
  queueLabel: string;
  jobs: JobRun[];
  activeCount: number;
  waitingCount: number;
  totalCount: number;
}

export interface FailedJobGroup {
  fingerprint: string;
  representative: JobRun;
  jobs: JobRun[];
  count: number;
  firstFailedAt: string | null;
  lastFailedAt: string | null;
}

export interface JobsDashboard {
  queues: QueueSummary[];
  activeJobs: JobRun[];
  failedJobs: JobRun[];
  completedJobs: JobRun[];
  recentJobs: JobRun[];
  lastScanAt: string | null;
  schedule: {
    enabled: boolean;
    intervalMinutes: number;
  };
}
