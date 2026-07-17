import {
  BookOpen,
  FolderSearch,
  Image,
  Music,
  RefreshCw,
  Search,
  type LucideIcon,
} from "@lucide/svelte";
import { JOB_TYPE, type JobTypeCode } from "$lib/api/generated/codes";
import type { QueueName } from "./models";

export interface RunCatalogEntry {
  jobType: JobTypeCode;
  queueName: QueueName;
  label: string;
  description: string;
  icon: LucideIcon;
}

export interface RunCatalogGroup {
  id: string;
  title: string;
  entries: readonly RunCatalogEntry[];
}

export const RUN_CATALOG: readonly RunCatalogGroup[] = [
  {
    id: "scans",
    title: "Scans",
    entries: [
      {
        jobType: JOB_TYPE.scanLibrary,
        queueName: "library-scan",
        label: "Videos",
        description: "Walk library roots for new video files.",
        icon: FolderSearch,
      },
      {
        jobType: JOB_TYPE.scanGallery,
        queueName: "gallery-scan",
        label: "Images",
        description: "Walk library roots for image galleries.",
        icon: Image,
      },
      {
        jobType: JOB_TYPE.scanBook,
        queueName: "book-scan",
        label: "Books",
        description: "Walk library roots for comic archives.",
        icon: BookOpen,
      },
      {
        jobType: JOB_TYPE.scanAudio,
        queueName: "audio-scan",
        label: "Audio",
        description: "Walk library roots for audio tracks.",
        icon: Music,
      },
    ],
  },
  {
    id: "maintenance",
    title: "Maintenance",
    entries: [
      {
        jobType: JOB_TYPE.refreshCollection,
        queueName: "collection-refresh",
        label: "Refresh collections",
        description: "Re-evaluate dynamic collection rules.",
        icon: RefreshCw,
      },
      {
        jobType: JOB_TYPE.monitoredSearch,
        queueName: "monitored-search",
        label: "Check monitored items",
        description: "Re-search wanted items and sync followed authors/artists now.",
        icon: Search,
      },
    ],
  },
];
