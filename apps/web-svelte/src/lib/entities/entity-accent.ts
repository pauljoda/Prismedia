import { ENTITY_KIND } from "$lib/api/generated/codes";
import { colors } from "@prismedia/ui-svelte";

export interface EntityAccent {
  primary: string;
  secondary: string;
}

export const PRISM_SPECTRUM = {
  ...colors.spectrum,
} as const;

export const PRISM_MATERIAL_SPECTRUM = {
  ...colors.materialSpectrum,
} as const;

const VIDEO = { primary: PRISM_MATERIAL_SPECTRUM.red, secondary: PRISM_MATERIAL_SPECTRUM.orange };
const MOVIE = { primary: PRISM_MATERIAL_SPECTRUM.orange, secondary: PRISM_MATERIAL_SPECTRUM.yellow };
const SERIES = { primary: PRISM_MATERIAL_SPECTRUM.yellow, secondary: PRISM_MATERIAL_SPECTRUM.green };
const GALLERY = { primary: PRISM_MATERIAL_SPECTRUM.green, secondary: PRISM_MATERIAL_SPECTRUM.cyan };
const BOOK = { primary: PRISM_MATERIAL_SPECTRUM.cyan, secondary: PRISM_MATERIAL_SPECTRUM.blue };
const IMAGE = { primary: PRISM_MATERIAL_SPECTRUM.blue, secondary: PRISM_MATERIAL_SPECTRUM.violet };
const AUDIO = { primary: PRISM_MATERIAL_SPECTRUM.violet, secondary: PRISM_MATERIAL_SPECTRUM.magenta };
const COLLECTION = { primary: PRISM_MATERIAL_SPECTRUM.magenta, secondary: PRISM_MATERIAL_SPECTRUM.red };
const PEOPLE = { primary: PRISM_MATERIAL_SPECTRUM.red, secondary: PRISM_MATERIAL_SPECTRUM.violet };
const STUDIOS = { primary: PRISM_MATERIAL_SPECTRUM.orange, secondary: PRISM_MATERIAL_SPECTRUM.magenta };
const TAGS = { primary: PRISM_MATERIAL_SPECTRUM.green, secondary: PRISM_MATERIAL_SPECTRUM.yellow };

const ENTITY_ACCENTS: Readonly<Record<string, EntityAccent>> = {
  [ENTITY_KIND.video]: VIDEO,
  [ENTITY_KIND.movie]: MOVIE,
  [ENTITY_KIND.videoSeries]: SERIES,
  [ENTITY_KIND.videoSeason]: SERIES,
  [ENTITY_KIND.gallery]: GALLERY,
  [ENTITY_KIND.book]: BOOK,
  [ENTITY_KIND.bookVolume]: BOOK,
  [ENTITY_KIND.bookChapter]: BOOK,
  [ENTITY_KIND.bookPage]: BOOK,
  [ENTITY_KIND.bookAuthor]: BOOK,
  [ENTITY_KIND.image]: IMAGE,
  [ENTITY_KIND.audio]: AUDIO,
  [ENTITY_KIND.audioLibrary]: AUDIO,
  [ENTITY_KIND.audioTrack]: AUDIO,
  [ENTITY_KIND.musicArtist]: AUDIO,
  [ENTITY_KIND.collection]: COLLECTION,
  [ENTITY_KIND.person]: PEOPLE,
  [ENTITY_KIND.studio]: STUDIOS,
  [ENTITY_KIND.tag]: TAGS,
};

const FALLBACK_ACCENT = { primary: PRISM_MATERIAL_SPECTRUM.cyan, secondary: PRISM_MATERIAL_SPECTRUM.violet };

/** Returns the stable spectrum pair that represents an entity family. */
export function entityAccentForKind(kind: string): EntityAccent {
  return ENTITY_ACCENTS[kind] ?? FALLBACK_ACCENT;
}
