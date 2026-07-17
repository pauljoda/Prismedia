import { ENTITY_KIND } from "$lib/entities/entity-codes";
import type { SearchEntityKind } from "$lib/search/models";
import {
  Building2,
  BookOpen,
  Clapperboard,
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

interface SearchKindConfig {
  label: string;
  icon: typeof Icon;
  href: string;
}

export const SEARCH_KIND_CONFIG: Record<SearchEntityKind, SearchKindConfig> = {
  [ENTITY_KIND.movie]: { label: "Movies", icon: Clapperboard, href: "/movies" },
  [ENTITY_KIND.videoSeries]: { label: "Series", icon: FolderOpen, href: "/series" },
  [ENTITY_KIND.video]: { label: "Videos", icon: Film, href: "/videos" },
  [ENTITY_KIND.person]: { label: "People", icon: Users, href: "/people" },
  [ENTITY_KIND.studio]: { label: "Studios", icon: Building2, href: "/studios" },
  [ENTITY_KIND.tag]: { label: "Tags", icon: Tag, href: "/tags" },
  [ENTITY_KIND.gallery]: { label: "Galleries", icon: Images, href: "/galleries" },
  [ENTITY_KIND.book]: { label: "Books", icon: BookOpen, href: "/books" },
  [ENTITY_KIND.image]: { label: "Images", icon: Image, href: "/images" },
  [ENTITY_KIND.collection]: { label: "Collections", icon: Layers3, href: "/collections" },
  [ENTITY_KIND.audioLibrary]: { label: "Audio Libraries", icon: Music, href: "/audio" },
  [ENTITY_KIND.audioTrack]: { label: "Audio Tracks", icon: Music, href: "/audio" },
};
