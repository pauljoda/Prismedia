import {
  BookOpen,
  FolderSearch,
  Image,
  Music,
  RefreshCw,
  Search,
  type LucideIcon,
} from "@lucide/svelte";

export interface RunCatalogEntry {
  jobType: string;
  queueName: string;
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
        jobType: "scan-library",
        queueName: "library-scan",
        label: "Videos",
        description: "Walk library roots for new video files.",
        icon: FolderSearch,
      },
      {
        jobType: "scan-gallery",
        queueName: "gallery-scan",
        label: "Images",
        description: "Walk library roots for image galleries.",
        icon: Image,
      },
      {
        jobType: "scan-book",
        queueName: "book-scan",
        label: "Books",
        description: "Walk library roots for comic archives.",
        icon: BookOpen,
      },
      {
        jobType: "scan-audio",
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
        jobType: "refresh-collection",
        queueName: "collection-refresh",
        label: "Refresh collections",
        description: "Re-evaluate dynamic collection rules.",
        icon: RefreshCw,
      },
      {
        jobType: "monitored-search",
        queueName: "monitored-search",
        label: "Check monitored items",
        description: "Re-search wanted items and sync followed authors/artists now.",
        icon: Search,
      },
    ],
  },
];
