import type { Component } from "svelte";
import { ENTITY_KIND, type EntityKindCode } from "$lib/api/generated/codes";
import { fetchEntities } from "$lib/api/entities";
import { entityAccentForKind, entityEmittedAccentForKind, type EntityAccent } from "$lib/entities/entity-accent";
import { entityKindIcon } from "$lib/entities/entity-kind-icons";

/** One media family of the library, as a band of the spectrum. */
export interface LibraryFamily {
  kind: EntityKindCode;
  label: string;
  href: string;
  count: number;
  /** Flat material paint for persistent chrome. */
  accent: EntityAccent;
  /** Brand spectrum pair for literal light moments (the prism beam). */
  emitted: EntityAccent;
  icon: Component;
}

/** Library families in spectrum order: the order white light separates into. */
const FAMILIES: ReadonlyArray<Pick<LibraryFamily, "kind" | "label" | "href">> = [
  { kind: ENTITY_KIND.video, label: "Videos", href: "/videos" },
  { kind: ENTITY_KIND.movie, label: "Movies", href: "/movies" },
  { kind: ENTITY_KIND.videoSeries, label: "Series", href: "/series" },
  { kind: ENTITY_KIND.gallery, label: "Galleries", href: "/galleries" },
  { kind: ENTITY_KIND.book, label: "Books", href: "/books" },
  { kind: ENTITY_KIND.comicSeries, label: "Comics", href: "/comics" },
  { kind: ENTITY_KIND.image, label: "Images", href: "/images" },
  { kind: ENTITY_KIND.audioLibrary, label: "Albums", href: "/audio" },
];

/** Counts every family of the library (one limit-1 query each) so a page can draw its composition. */
export async function loadLibraryComposition(hideNsfw: boolean, signal?: AbortSignal): Promise<LibraryFamily[]> {
  const counts = await Promise.all(FAMILIES.map((family) =>
    fetchEntities({ kind: family.kind, limit: 1, hideNsfw }, { signal })
      .then((page) => Number(page.totalCount) || 0)
      .catch(() => 0)));
  return FAMILIES.map((family, index) => ({
    ...family,
    count: counts[index] ?? 0,
    accent: entityAccentForKind(family.kind),
    emitted: entityEmittedAccentForKind(family.kind),
    icon: entityKindIcon(family.kind),
  }));
}

/** Total items across the families. */
export function libraryTotal(families: readonly LibraryFamily[]): number {
  return families.reduce((sum, family) => sum + family.count, 0);
}
