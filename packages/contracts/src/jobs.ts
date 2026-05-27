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
    description: "Generates md5 and oshash fingerprints for videos",
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
    description: "Computes md5 and oshash fingerprints for images",
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
    description: "Computes md5 and oshash fingerprints for audio tracks",
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
    name: "plugin-batch-identify",
    label: "Plugin Batch Identify",
    description: "Batch metadata identification via Prismedia plugins",
    concurrency: 1,
  },
] as const;

/** BullMQ jobs per queue; default 1. Higher values increase CPU, disk, and memory use. */
export const BACKGROUND_WORKER_CONCURRENCY_MIN = 1;
export const BACKGROUND_WORKER_CONCURRENCY_MAX = 32;

export function normalizeBackgroundWorkerConcurrency(raw: unknown): number {
  const n = typeof raw === "number" ? raw : Number(raw);
  if (!Number.isFinite(n)) {
    return BACKGROUND_WORKER_CONCURRENCY_MIN;
  }
  const floor = Math.floor(n);
  return Math.min(
    BACKGROUND_WORKER_CONCURRENCY_MAX,
    Math.max(BACKGROUND_WORKER_CONCURRENCY_MIN, floor),
  );
}

/** Effective BullMQ concurrency for a queue: definition base x normalized user setting. */
export function resolveQueueWorkerConcurrency(
  definitionConcurrency: number,
  backgroundWorkerConcurrency: unknown,
): number {
  const base = Math.max(1, Math.floor(definitionConcurrency));
  const k = normalizeBackgroundWorkerConcurrency(backgroundWorkerConcurrency);
  return base * k;
}

export type QueueName = (typeof queueDefinitions)[number]["name"];

export const jobRunRetention = {
  completed: 40,
  dismissed: 40,
} as const;

export const jobStatuses = [
  "waiting",
  "active",
  "completed",
  "failed",
  "dismissed",
  "delayed",
  "paused",
] as const;

export type JobStatus = (typeof jobStatuses)[number];

export const jobTriggerKinds = [
  "manual",
  "schedule",
  "library-scan",
  "gallery-scan",
  "book-scan",
  "audio-scan",
  "system",
] as const;

export type JobTriggerKind = (typeof jobTriggerKinds)[number];

export const jobKinds = ["standard", "force-rebuild"] as const;

export type JobKind = (typeof jobKinds)[number];
