import type { Component } from "svelte";
import {
  BookOpen,
  Disc3,
  Film,
  FolderOpen,
  Images,
  Image,
  Layers,
  Music,
  Tag,
  Users,
  Building2,
} from "@lucide/svelte";

const KIND_ICON_MAP: Record<string, Component> = {
  video: Film,
  "video-series": FolderOpen,
  "video-season": Layers,
  book: BookOpen,
  "book-volume": BookOpen,
  "book-chapter": BookOpen,
  "audio-library": Disc3,
  "audio-track": Music,
  gallery: Images,
  image: Image,
  person: Users,
  studio: Building2,
  collection: Layers,
  tag: Tag,
};

export function entityKindIcon(kind: string): Component {
  return KIND_ICON_MAP[kind] ?? Film;
}
