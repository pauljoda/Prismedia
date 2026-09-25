import type { LedStatus } from "@prismedia/ui-svelte";
import {
  ENTITY_KIND,
  LIBRARY_ROOT_MEDIA_CAPABILITY,
  type EntityKindCode,
  type LibraryRootMediaCapabilityCode,
} from "$lib/api/generated/codes";
import type { LibraryRoot } from "$lib/api/settings";
import { formatRelativeTime } from "$lib/utils/format";
import { mediaFamilyForKind, mediaFamilyOrder, type SpectrumBand } from "./media-families";

/** Which media family each library-root scan switch feeds. */
const ROOT_MEDIA: ReadonlyArray<{ capability: LibraryRootMediaCapabilityCode; kind: EntityKindCode; label: string }> = [
  { capability: LIBRARY_ROOT_MEDIA_CAPABILITY.scanVideos, kind: ENTITY_KIND.video, label: "Video" },
  { capability: LIBRARY_ROOT_MEDIA_CAPABILITY.scanBooks, kind: ENTITY_KIND.book, label: "Books & comics" },
  { capability: LIBRARY_ROOT_MEDIA_CAPABILITY.scanImages, kind: ENTITY_KIND.image, label: "Images" },
  { capability: LIBRARY_ROOT_MEDIA_CAPABILITY.scanAudio, kind: ENTITY_KIND.audioLibrary, label: "Audio" },
];

/** The media a library root is configured to scan, as spectrum bands in spectrum order. */
export function libraryRootBands(root: LibraryRoot): SpectrumBand[] {
  return ROOT_MEDIA.filter((media) => root[media.capability])
    .map((media) => ({ media, family: mediaFamilyForKind(media.kind) }))
    .sort((left, right) => mediaFamilyOrder(left.family) - mediaFamilyOrder(right.family))
    .map(({ media, family }) => ({ key: media.capability, label: media.label, accent: family.accent }));
}

/** A library root's operating state in one LED and a few words. */
export function libraryRootState(root: LibraryRoot): { led: LedStatus; label: string } {
  if (!root.enabled) return { led: "idle", label: "Paused" };
  if (!root.lastScannedAt) return { led: "warning", label: "Never scanned" };
  return { led: "phosphor", label: formatRelativeTime(root.lastScannedAt) };
}

/** The newest scan across a set of roots, or null when none has been scanned. */
export function latestRootScan(roots: readonly LibraryRoot[]): string | null {
  let latest: string | null = null;
  for (const root of roots) {
    if (root.lastScannedAt && (!latest || Date.parse(root.lastScannedAt) > Date.parse(latest))) {
      latest = root.lastScannedAt;
    }
  }
  return latest;
}
