import type { QueueName } from "./models";

export type JobQueueSection = {
  id: string;
  title: string;
  description: string;
  queueNames: readonly QueueName[];
};

/**
 * Static layout for the job control page: scans first, then groups by kind of work.
 * Order does not change with queue pressure or failures.
 */
export const JOB_QUEUE_SECTIONS: readonly JobQueueSection[] = [
  {
    id: "scans",
    title: "Video scans",
    description: "Video discovery across configured media roots.",
    queueNames: ["library-scan", "gallery-scan", "book-scan", "audio-scan"],
  },
  {
    id: "maintenance",
    title: "Video maintenance",
    description: "One-off video tasks such as relocating generated files.",
    queueNames: ["library-maintenance"],
  },
  {
    id: "video-media",
    title: "Video media pipeline",
    description: "Technical metadata, fingerprints, and previews for videos.",
    queueNames: ["media-probe", "fingerprint", "preview"],
  },
  {
    id: "metadata",
    title: "Metadata import",
    description: "Provider imports and applying metadata to the library.",
    queueNames: ["metadata-import"],
  },
  {
    id: "gallery-images",
    title: "Image and book pipeline",
    description: "Thumbnails and fingerprints for gallery images and book pages.",
    queueNames: ["image-thumbnail", "image-fingerprint", "book-page-thumbnail"],
  },
  {
    id: "audio-pipeline",
    title: "Audio pipeline",
    description:
      "Metadata extraction, fingerprints, and waveform generation for audio tracks.",
    queueNames: ["audio-probe", "audio-fingerprint", "audio-waveform"],
  },
] as const;

const SECTION_NAME_SET = new Set<QueueName>(
  JOB_QUEUE_SECTIONS.flatMap((section) => [...section.queueNames]),
);

export type QueueSummaryLike = { name: string; label: string };

export function groupQueuesForJobDashboard<T extends QueueSummaryLike>(
  queues: readonly T[],
): { section: JobQueueSection | null; queues: T[] }[] {
  const byName = new Map(queues.map((q) => [q.name, q] as const));
  const grouped: { section: JobQueueSection | null; queues: T[] }[] = [];

  for (const section of JOB_QUEUE_SECTIONS) {
    const sectionQueues = section.queueNames
      .map((name) => byName.get(name))
      .filter((q): q is T => q !== undefined);
    if (sectionQueues.length > 0) {
      grouped.push({ section, queues: sectionQueues });
    }
  }

  const unknown = queues
    .filter((q) => !SECTION_NAME_SET.has(q.name as QueueName))
    .slice()
    .sort((a, b) => a.label.localeCompare(b.label));

  if (unknown.length > 0) {
    grouped.push({
      section: null,
      queues: unknown,
    });
  }

  return grouped;
}
