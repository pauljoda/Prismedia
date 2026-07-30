import {
  AUDIO_QUALITY,
  ENTITY_KIND,
  ENTITY_KIND_DEFINITIONS,
  ENTITY_MEDIA_QUALITY_FAMILY,
  LIBRARY_ROOT_MEDIA_CAPABILITY,
  VIDEO_QUALITY,
} from "$lib/api/generated/codes";
import type { LibraryRootSummary } from "$lib/api/settings";
import { isEntityKindCode } from "$lib/entities/entity-codes";

function profileFor(kind: string) {
  return isEntityKindCode(kind) ? ENTITY_KIND_DEFINITIONS[kind].acquisitionProfile : null;
}

export const DEFAULT_PATH_TEMPLATE = profileFor(ENTITY_KIND.book)?.defaultNamingTemplate ?? "";

export function namingDefaultFor(kind: string): string {
  return profileFor(kind)?.defaultNamingTemplate ?? DEFAULT_PATH_TEMPLATE;
}

export function namingHintFor(kind: string): string {
  return profileFor(kind)?.namingHint ?? "";
}

const videoQualityLadder = Object.values(VIDEO_QUALITY).filter((code) => code !== VIDEO_QUALITY.unknown);
const audioQualityLadder = Object.values(AUDIO_QUALITY).filter((code) => code !== AUDIO_QUALITY.unknown);

/** Quality ladder projected by the canonical Entity-kind definition. */
export function qualityLadderFor(kind: string): string[] {
  const family = isEntityKindCode(kind)
    ? ENTITY_KIND_DEFINITIONS[kind].mediaQualityFamily
    : ENTITY_MEDIA_QUALITY_FAMILY.none;
  return family === ENTITY_MEDIA_QUALITY_FAMILY.video
    ? videoQualityLadder
    : family === ENTITY_MEDIA_QUALITY_FAMILY.audio
      ? audioQualityLadder
      : [];
}

export const profileKindOptions = Object.values(ENTITY_KIND_DEFINITIONS)
  .filter((definition) => definition.acquisitionProfile !== null)
  .sort((left, right) => left.acquisitionProfile!.displayOrder - right.acquisitionProfile!.displayOrder)
  .map((definition) => ({
    value: definition.kind,
    label: definition.acquisitionProfile!.label,
  }));
export const profileKindLabels: Readonly<Record<string, string>> = Object.fromEntries(
  profileKindOptions.map((option) => [option.value, option.label]),
);

/** Filters library roots using the acquisition-profile facet that owns each profile kind. */
export function rootsForProfileKind(roots: LibraryRootSummary[], kind: string): LibraryRootSummary[] {
  const capability = profileFor(kind)?.libraryRootMediaCapability;
  return roots.filter((root) =>
    capability === LIBRARY_ROOT_MEDIA_CAPABILITY.scanVideos
      ? root.scanVideos
      : capability === LIBRARY_ROOT_MEDIA_CAPABILITY.scanAudio
        ? root.scanAudio
        : capability === LIBRARY_ROOT_MEDIA_CAPABILITY.scanBooks && root.scanBooks);
}
