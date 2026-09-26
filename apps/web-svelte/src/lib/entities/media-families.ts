import type { Component } from "svelte";
import { ENTITY_KIND, type EntityKindCode } from "$lib/api/generated/codes";
import { entityAccentForKind, type EntityAccent } from "$lib/entities/entity-accent";
import { labelForEntityKind } from "$lib/entities/entity-codes";
import { entityKindIcon } from "$lib/entities/entity-kind-icons";

/** One band of the spectrum as drawn by the management concepts: a family, its paint, and an optional weight. */
export interface SpectrumBand {
  key: string;
  label: string;
  accent: EntityAccent;
  /** Relative size of the band; bands without a weight share the width equally. */
  weight?: number;
}

/** A media family as the design language names it, anchored on the entity kind that defines its hue pair. */
export interface MediaFamily {
  key: string;
  label: string;
  anchorKind: string;
  accent: EntityAccent;
  icon: Component;
}

/**
 * Family anchors in spectrum order. Every entity kind shares its hue pair with exactly one anchor,
 * so a season joins Series, an author joins Books, and an album track joins Music.
 */
const FAMILY_ANCHORS: ReadonlyArray<{ kind: EntityKindCode; label: string }> = [
  { kind: ENTITY_KIND.video, label: "Video" },
  { kind: ENTITY_KIND.movie, label: "Movies" },
  { kind: ENTITY_KIND.videoSeries, label: "Series" },
  { kind: ENTITY_KIND.gallery, label: "Galleries" },
  { kind: ENTITY_KIND.book, label: "Books & comics" },
  { kind: ENTITY_KIND.image, label: "Images" },
  { kind: ENTITY_KIND.audioLibrary, label: "Music" },
  { kind: ENTITY_KIND.collection, label: "Collections" },
  { kind: ENTITY_KIND.person, label: "People" },
  { kind: ENTITY_KIND.studio, label: "Studios" },
  { kind: ENTITY_KIND.tag, label: "Tags" },
];

function hueKey(kind: string): string {
  const accent = entityAccentForKind(kind);
  return `${accent.primary}>${accent.secondary}`;
}

/** Every named media family, in the order dispersed light arrives. */
export const MEDIA_FAMILIES: readonly MediaFamily[] = FAMILY_ANCHORS.map((anchor) => ({
  key: anchor.kind,
  label: anchor.label,
  anchorKind: anchor.kind,
  accent: entityAccentForKind(anchor.kind),
  icon: entityKindIcon(anchor.kind),
}));

const FAMILY_BY_HUE = new Map(MEDIA_FAMILIES.map((family) => [hueKey(family.anchorKind), family]));

/** The named family an entity kind belongs to; unknown kinds become a family of their own. */
export function mediaFamilyForKind(kind: string): MediaFamily {
  return (
    FAMILY_BY_HUE.get(hueKey(kind)) ?? {
      key: kind,
      label: labelForEntityKind(kind),
      anchorKind: kind,
      accent: entityAccentForKind(kind),
      icon: entityKindIcon(kind),
    }
  );
}

/** Position of a family along the spectrum; unknown families sort last. */
export function mediaFamilyOrder(family: MediaFamily): number {
  const index = MEDIA_FAMILIES.findIndex((candidate) => candidate.key === family.key);
  return index < 0 ? MEDIA_FAMILIES.length : index;
}

/** Counts entity kinds into weighted family bands, in spectrum order. */
export function bandsFromKinds(kinds: readonly string[]): SpectrumBand[] {
  const weights = new Map<string, { family: MediaFamily; weight: number }>();
  for (const kind of kinds) {
    const family = mediaFamilyForKind(kind);
    const current = weights.get(family.key);
    if (current) current.weight += 1;
    else weights.set(family.key, { family, weight: 1 });
  }
  return [...weights.values()]
    .sort((left, right) => mediaFamilyOrder(left.family) - mediaFamilyOrder(right.family))
    .map(({ family, weight }) => ({ key: family.key, label: family.label, accent: family.accent, weight }));
}

/** CSS gradient for one band in flat material paint. */
export function bandGradient(accent: EntityAccent): string {
  return `linear-gradient(90deg, ${accent.primary}, ${accent.secondary})`;
}
