import {
  BookCopy,
  BookOpen,
  FolderSearch,
  Image,
  Music,
  RefreshCw,
  Search,
  type LucideIcon,
} from "@lucide/svelte";
import { JOB_TYPE, type JobTypeCode } from "$lib/api/generated/codes";

export interface RunCatalogEntry {
  jobType: JobTypeCode;
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
        label: "Videos",
        description: "Walk library roots for new video files.",
        icon: FolderSearch,
      },
      {
        jobType: JOB_TYPE.scanGallery,
        label: "Images",
        description: "Walk library roots for image galleries.",
        icon: Image,
      },
      {
        jobType: JOB_TYPE.scanBook,
        label: "Books",
        description: "Walk library roots for prose books and audiobook sources.",
        icon: BookOpen,
      },
      {
        jobType: JOB_TYPE.scanComic,
        label: "Comics",
        description: "Walk library roots for serialized comic archives and loose-page folders.",
        icon: BookCopy,
      },
      {
        jobType: JOB_TYPE.scanAudio,
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
        label: "Refresh collections",
        description: "Re-evaluate dynamic collection rules.",
        icon: RefreshCw,
      },
      {
        jobType: JOB_TYPE.monitoredSearch,
        label: "Check monitored items",
        description: "Re-search wanted items and sync followed authors/artists now.",
        icon: Search,
      },
    ],
  },
];
