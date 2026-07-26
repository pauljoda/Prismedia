import type { Component } from "svelte";
import {
  BookOpen,
  Clapperboard,
  Disc3,
  Film,
  FolderOpen,
  Headphones,
  Layers,
  MicVocal,
  Music,
  Users,
} from "@lucide/svelte";
import {
  ENTITY_KIND,
  REQUEST_MEDIA_KIND,
  type RequestMediaKindCode,
} from "$lib/api/generated/codes";
import { entityAccentForKind } from "$lib/entities/entity-accent";
import { requestKindInfo } from "$lib/requests/request-helpers";

const REQUEST_KIND_ICONS: Readonly<Partial<Record<RequestMediaKindCode, Component>>> = {
  [REQUEST_MEDIA_KIND.book]: BookOpen,
  [REQUEST_MEDIA_KIND.audiobook]: Headphones,
  [REQUEST_MEDIA_KIND.author]: Users,
  [REQUEST_MEDIA_KIND.movie]: Clapperboard,
  [REQUEST_MEDIA_KIND.series]: FolderOpen,
  [REQUEST_MEDIA_KIND.season]: Layers,
  [REQUEST_MEDIA_KIND.episode]: Film,
  [REQUEST_MEDIA_KIND.artist]: MicVocal,
  [REQUEST_MEDIA_KIND.album]: Disc3,
  [REQUEST_MEDIA_KIND.track]: Music,
};

/** Resolves the stable symbol used for a request kind wherever it is selectable. */
export function requestKindIcon(kind: RequestMediaKindCode): Component {
  return REQUEST_KIND_ICONS[kind] ?? Film;
}

/** Uses the entity-family spectrum, treating audiobooks as audio for presentation. */
export function requestKindAccent(kind: RequestMediaKindCode): string {
  const entityKind = kind === REQUEST_MEDIA_KIND.audiobook
    ? ENTITY_KIND.audio
    : requestKindInfo(kind)?.entityKind ?? ENTITY_KIND.book;
  return entityAccentForKind(entityKind).primary;
}
