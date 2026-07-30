import type { Component } from "svelte";
import {
  Album,
  BookOpen,
  BookCopy,
  Building2,
  Calendar,
  Clapperboard,
  Clock3,
  Disc3,
  FileText,
  Film,
  FolderOpen,
  Hash,
  Image,
  Images,
  Layers,
  ListMusic,
  MicVocal,
  Music,
  Tag,
  Users,
} from "@lucide/svelte";
import {
  ENTITY_KIND_ICON,
  ENTITY_KIND_DEFINITIONS,
  THUMBNAIL_META_ICON,
  type EntityKindIconCode,
} from "$lib/api/generated/codes";
import { isEntityKindCode } from "./entity-codes";

const ICON_COMPONENTS: Record<EntityKindIconCode, Component> = {
  [ENTITY_KIND_ICON.album]: Disc3,
  [ENTITY_KIND_ICON.artist]: MicVocal,
  [ENTITY_KIND_ICON.audio]: Disc3,
  [ENTITY_KIND_ICON.author]: Users,
  [ENTITY_KIND_ICON.book]: BookOpen,
  [ENTITY_KIND_ICON.chapter]: BookOpen,
  [ENTITY_KIND_ICON.collection]: Layers,
  [ENTITY_KIND_ICON.gallery]: Images,
  [ENTITY_KIND_ICON.image]: Image,
  [ENTITY_KIND_ICON.movie]: Clapperboard,
  [ENTITY_KIND_ICON.page]: BookOpen,
  [ENTITY_KIND_ICON.person]: Users,
  [ENTITY_KIND_ICON.season]: Layers,
  [ENTITY_KIND_ICON.series]: FolderOpen,
  [ENTITY_KIND_ICON.studio]: Building2,
  [ENTITY_KIND_ICON.tag]: Tag,
  [ENTITY_KIND_ICON.track]: Music,
  [ENTITY_KIND_ICON.video]: Film,
  [ENTITY_KIND_ICON.volume]: BookOpen,
};

/** Resolves a shared Lucide icon for an entity kind code. */
export function entityKindIcon(kind: string): Component {
  return isEntityKindCode(kind)
    ? ICON_COMPONENTS[ENTITY_KIND_DEFINITIONS[kind].presentation.icon]
    : Film;
}

const THUMBNAIL_META_ICON_COMPONENTS: Readonly<Record<string, Component>> = {
  [THUMBNAIL_META_ICON.album]: Disc3,
  [THUMBNAIL_META_ICON.audio]: Music,
  [THUMBNAIL_META_ICON.book]: BookOpen,
  [THUMBNAIL_META_ICON.calendar]: Calendar,
  [THUMBNAIL_META_ICON.chapter]: Album,
  [THUMBNAIL_META_ICON.collection]: Layers,
  [THUMBNAIL_META_ICON.duration]: Clock3,
  [THUMBNAIL_META_ICON.episode]: Clapperboard,
  [THUMBNAIL_META_ICON.gallery]: Images,
  [THUMBNAIL_META_ICON.image]: Images,
  [THUMBNAIL_META_ICON.page]: FileText,
  [THUMBNAIL_META_ICON.person]: Users,
  [THUMBNAIL_META_ICON.season]: Calendar,
  [THUMBNAIL_META_ICON.studio]: Building2,
  [THUMBNAIL_META_ICON.tag]: Tag,
  [THUMBNAIL_META_ICON.track]: ListMusic,
  [THUMBNAIL_META_ICON.video]: Film,
  [THUMBNAIL_META_ICON.volume]: BookCopy,
};

const THUMBNAIL_PLACEHOLDER_ICON_COMPONENTS: Readonly<Record<string, Component>> = {
  [THUMBNAIL_META_ICON.book]: BookOpen,
  [THUMBNAIL_META_ICON.collection]: FolderOpen,
  [THUMBNAIL_META_ICON.gallery]: Layers,
  [THUMBNAIL_META_ICON.image]: Image,
  [THUMBNAIL_META_ICON.person]: Users,
  [THUMBNAIL_META_ICON.studio]: Building2,
  [THUMBNAIL_META_ICON.tag]: Tag,
};

/** Resolves the compact glyph used by thumbnail metadata chips. */
export function thumbnailMetaIcon(icon: string): Component {
  return THUMBNAIL_META_ICON_COMPONENTS[icon] ?? Hash;
}

/** Resolves the large neutral glyph used by thumbnail artwork placeholders. */
export function thumbnailPlaceholderIcon(icon: string): Component {
  return THUMBNAIL_PLACEHOLDER_ICON_COMPONENTS[icon] ?? Hash;
}
