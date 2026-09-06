import type {CSSProperties} from 'react';
import {
  ENTITY_KIND,
  ENTITY_KIND_DEFINITIONS,
  type EntityKindCode,
} from '../../../../apps/web-svelte/src/lib/api/generated/codes';
import {colors} from '../../../../packages/ui-svelte/src/tokens/colors';

// Editorial labels choose what to show; identity and both palettes come from the app.
const families = [
  {kind: ENTITY_KIND.video, label: 'Videos', detail: 'Your own footage', href: '/docs/library/videos'},
  {kind: ENTITY_KIND.movie, label: 'Movies', detail: 'A place for every film', href: '/docs/library/videos'},
  {kind: ENTITY_KIND.videoSeries, label: 'TV & series', detail: 'Seasons and episodes', href: '/docs/library/videos'},
  {kind: ENTITY_KIND.gallery, label: 'Galleries', detail: 'Images, collected', href: '/docs/library/images-galleries'},
  {kind: ENTITY_KIND.book, label: 'Books & comics', detail: 'Read or listen', href: '/docs/library/books'},
  {kind: ENTITY_KIND.image, label: 'Images', detail: 'Room for the details', href: '/docs/library/images-galleries'},
  {kind: ENTITY_KIND.audio, label: 'Music', detail: 'Artists, albums, tracks', href: '/docs/library/audio'},
  {kind: ENTITY_KIND.collection, label: 'Collections', detail: 'Connections across types', href: '/docs/using/collections'},
] as const;

/** Resolves an Entity family's material marker and emitted beam colors from canonical definitions. */
export function familyStyle(kind: EntityKindCode): CSSProperties {
  const {primaryAccent, secondaryAccent} = ENTITY_KIND_DEFINITIONS[kind].presentation;
  return {
    '--family-color': colors.materialSpectrum[primaryAccent],
    '--family-secondary': colors.materialSpectrum[secondaryAccent],
    '--beam-color': colors.spectrum[primaryAccent],
    '--beam-secondary': colors.spectrum[secondaryAccent],
  } as CSSProperties;
}

export const MEDIA_FAMILIES = families.map((family) => ({
  ...family,
  style: familyStyle(family.kind),
}));

export {ENTITY_KIND};
