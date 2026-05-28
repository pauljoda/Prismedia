import type { SearchEntityKind } from "$lib/search/models";
import {
  Building2,
  BookOpen,
  Film,
  FolderOpen,
  Image,
  Images,
  Layers3,
  Music,
  Tag,
  Users,
  type Icon,
} from "@lucide/svelte";

export const ALL_SEARCH_KINDS: SearchEntityKind[] = [
  "video-series",
  "video",
  "performer",
  "studio",
  "tag",
  "gallery",
  "book",
  "image",
  "collection",
  "audio-library",
  "audio-track",
];

interface SearchKindConfig {
  label: string;
  icon: typeof Icon;
  href: string;
}

export const SEARCH_KIND_CONFIG: Record<SearchEntityKind, SearchKindConfig> = {
  "video-series": { label: "Series", icon: FolderOpen, href: "/series" },
  video: { label: "Videos", icon: Film, href: "/videos" },
  performer: { label: "People", icon: Users, href: "/people" },
  studio: { label: "Studios", icon: Building2, href: "/studios" },
  tag: { label: "Tags", icon: Tag, href: "/tags" },
  gallery: { label: "Galleries", icon: Images, href: "/galleries" },
  book: { label: "Books", icon: BookOpen, href: "/books" },
  image: { label: "Images", icon: Image, href: "/images" },
  collection: { label: "Collections", icon: Layers3, href: "/collections" },
  "audio-library": { label: "Audio Libraries", icon: Music, href: "/audio" },
  "audio-track": { label: "Audio Tracks", icon: Music, href: "/audio" },
};
