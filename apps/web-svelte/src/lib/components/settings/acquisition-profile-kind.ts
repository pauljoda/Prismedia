import {
  AUDIO_QUALITY,
  ENTITY_KIND,
  ENTITY_KIND_DEFINITIONS,
  ENTITY_MEDIA_QUALITY_FAMILY,
  LIBRARY_ROOT_MEDIA_CAPABILITY,
  REQUEST_KIND_MANIFEST,
  VIDEO_QUALITY,
  type EntityKindCode,
  type LibraryRootMediaCapabilityCode,
} from "$lib/api/generated/codes";
import type { LibraryRootSummary } from "$lib/api/settings";
import { isEntityKindCode } from "$lib/entities/entity-codes";

export const DEFAULT_PATH_TEMPLATE = "{Author}/{Title} ({Year})/{Title}{ - Volume}.{ext}";

const NAMING_DEFAULTS: Partial<Record<EntityKindCode, string>> = {
  [ENTITY_KIND.book]: DEFAULT_PATH_TEMPLATE,
  [ENTITY_KIND.movie]: "{Title} ({Year})/{Title} ({Year}).{ext}",
  [ENTITY_KIND.videoSeries]: "{Series}/Season {Season:00}/{Series} - S{Season:00}E{Episode:00}.{ext}",
  [ENTITY_KIND.audioLibrary]: "{Artist}/{Album}",
};

const NAMING_HINTS: Partial<Record<EntityKindCode, string>> = {
  [ENTITY_KIND.book]: "{Author} {Title} {Year} {ext} — folder/file layout for the book payload",
  [ENTITY_KIND.movie]: "{Title} {Year} {Quality} {ext} — 2 segments: folder/file",
  [ENTITY_KIND.videoSeries]: "{Series} {Season} {Season:00} {Episode:00} {Quality} {ext} — 3 segments: series/season/episode",
  [ENTITY_KIND.audioLibrary]: "{Artist} {Album} {Year} — 2 segments: artist/album folder (track files keep their release names)",
};

export function namingDefaultFor(kind: string): string {
  return isEntityKindCode(kind) ? NAMING_DEFAULTS[kind] ?? DEFAULT_PATH_TEMPLATE : DEFAULT_PATH_TEMPLATE;
}

export function namingHintFor(kind: string): string {
  return isEntityKindCode(kind) ? NAMING_HINTS[kind] ?? "" : "";
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

const profileKinds = [...new Set(
  REQUEST_KIND_MANIFEST
    .map((request) => request.profileKind)
    .filter((kind): kind is EntityKindCode => kind != null),
)];
const PROFILE_LABEL_OVERRIDES: Partial<Record<EntityKindCode, string>> = {
  [ENTITY_KIND.videoSeries]: "TV (series)",
  [ENTITY_KIND.audioLibrary]: "Music (albums)",
};

export const profileKindOptions = profileKinds.map((kind) => ({
  value: kind,
  label: PROFILE_LABEL_OVERRIDES[kind] ?? ENTITY_KIND_DEFINITIONS[kind].groupLabel,
}));
export const profileKindLabels: Readonly<Record<string, string>> = Object.fromEntries(
  profileKindOptions.map((option) => [option.value, option.label]),
);

function rootCapabilityFor(kind: string): LibraryRootMediaCapabilityCode | null {
  const capabilities = new Set(
    REQUEST_KIND_MANIFEST
      .filter((request) => request.profileKind === kind)
      .map((request) => request.rootFlag)
      .filter((capability): capability is LibraryRootMediaCapabilityCode => capability != null),
  );
  return capabilities.size === 1 ? [...capabilities][0] : null;
}

/** Filters library roots using the request contract that owns each profile kind. */
export function rootsForProfileKind(roots: LibraryRootSummary[], kind: string): LibraryRootSummary[] {
  const capability = rootCapabilityFor(kind);
  return roots.filter((root) =>
    capability === LIBRARY_ROOT_MEDIA_CAPABILITY.scanVideos
      ? root.scanVideos
      : capability === LIBRARY_ROOT_MEDIA_CAPABILITY.scanAudio
        ? root.scanAudio
        : root.scanBooks);
}
