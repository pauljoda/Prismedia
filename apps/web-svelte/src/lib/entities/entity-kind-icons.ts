import type { Component } from "svelte";
import {
  BookOpen,
  Building2,
  Clapperboard,
  Disc3,
  Film,
  FolderOpen,
  Image,
  Images,
  Layers,
  MicVocal,
  Music,
  Tag,
  Users,
} from "@lucide/svelte";
import { ENTITY_KIND } from "./entity-codes";

const KIND_ICON_MAP: Partial<Record<string, Component>> = {
  [ENTITY_KIND.audio]: Disc3,
  [ENTITY_KIND.audioLibrary]: Disc3,
  [ENTITY_KIND.audioTrack]: Music,
  [ENTITY_KIND.book]: BookOpen,
  [ENTITY_KIND.bookAuthor]: Users,
  [ENTITY_KIND.bookChapter]: BookOpen,
  [ENTITY_KIND.bookPage]: BookOpen,
  [ENTITY_KIND.bookVolume]: BookOpen,
  [ENTITY_KIND.collection]: Layers,
  [ENTITY_KIND.gallery]: Images,
  [ENTITY_KIND.image]: Image,
  [ENTITY_KIND.movie]: Clapperboard,
  [ENTITY_KIND.musicArtist]: MicVocal,
  [ENTITY_KIND.person]: Users,
  [ENTITY_KIND.studio]: Building2,
  [ENTITY_KIND.tag]: Tag,
  [ENTITY_KIND.video]: Film,
  [ENTITY_KIND.videoSeason]: Layers,
  [ENTITY_KIND.videoSeries]: FolderOpen,
};

/** Resolves a shared Lucide icon for an entity kind code. */
export function entityKindIcon(kind: string): Component {
  return KIND_ICON_MAP[kind] ?? Film;
}
