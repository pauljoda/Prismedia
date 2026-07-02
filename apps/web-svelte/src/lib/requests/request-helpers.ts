import { ENTITY_KIND, REQUEST_MEDIA_KIND, REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
import type { EntityKindCode, RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";

/** Display names for request provider families. */
export const REQUEST_PROVIDER_LABELS: Record<string, string> = {
  [REQUEST_PROVIDER_KIND.plugin]: "Plugin",
};

/**
 * UI-side view of the backend's RequestKindRegistry: labels and per-kind flow hints for each
 * requestable kind, in Discover display order. `committable: false` kinds (series, until the TV
 * engine lands) are discoverable/browsable but offer no Request action. `childNoun` names a
 * container's selectable works ("book", "album") and a book's sibling volumes.
 */
export interface RequestKindInfo {
  kind: RequestMediaKindCode;
  label: string;
  plural: string;
  committable: boolean;
  childNoun: string | null;
  /** The acquisition-profile kind this request's downloads are governed by (null while not committable). */
  profileKind: EntityKindCode | null;
  /** The library-root capability flag the acquired files need ("scanBooks" | "scanVideos" | "scanAudio"). */
  rootFlag: "scanBooks" | "scanVideos" | "scanAudio" | null;
}

export const REQUEST_KINDS: RequestKindInfo[] = [
  { kind: REQUEST_MEDIA_KIND.book, label: "Book", plural: "Books", committable: true, childNoun: "volume", profileKind: ENTITY_KIND.book, rootFlag: "scanBooks" },
  { kind: REQUEST_MEDIA_KIND.author, label: "Author", plural: "Authors", committable: true, childNoun: "book", profileKind: ENTITY_KIND.book, rootFlag: "scanBooks" },
  { kind: REQUEST_MEDIA_KIND.movie, label: "Movie", plural: "Movies", committable: true, childNoun: null, profileKind: ENTITY_KIND.movie, rootFlag: "scanVideos" },
  { kind: REQUEST_MEDIA_KIND.series, label: "Series", plural: "Series", committable: false, childNoun: null, profileKind: null, rootFlag: null },
  { kind: REQUEST_MEDIA_KIND.artist, label: "Artist", plural: "Artists", committable: true, childNoun: "album", profileKind: ENTITY_KIND.audioLibrary, rootFlag: "scanAudio" },
  { kind: REQUEST_MEDIA_KIND.album, label: "Album", plural: "Albums", committable: true, childNoun: null, profileKind: ENTITY_KIND.audioLibrary, rootFlag: "scanAudio" },
];

export function requestKindInfo(kind: RequestMediaKindCode): RequestKindInfo | null {
  return REQUEST_KINDS.find((info) => info.kind === kind) ?? null;
}

/** Singular display names for request media kinds. */
export const REQUEST_KIND_LABELS: Record<string, string> = Object.fromEntries(
  REQUEST_KINDS.map((info) => [info.kind, info.label]),
);

/** Plural display names for request media kinds, used by filters and section headings. */
export const REQUEST_KIND_LABELS_PLURAL: Record<string, string> = Object.fromEntries(
  REQUEST_KINDS.map((info) => [info.kind, info.plural]),
);

/** "In Library"-style label for items already tracked. */
export function trackedLabel(source: RequestProviderKindCode): string {
  return `In ${REQUEST_PROVIDER_LABELS[source] ?? source}`;
}

/**
 * CSS aspect-ratio for a request result's artwork. Every requestable kind renders as a 2:3 portrait
 * poster (movie/series posters, book covers, album art scaled into the same frame).
 */
export function thumbnailAspectForKind(_kind: RequestMediaKindCode): string {
  return "2 / 3";
}

export function inferRequestSourceForKind(kind: RequestMediaKindCode): RequestProviderKindCode | null {
  // Every registered kind is fulfilled by the Prismedia plugin acquisition path; this is the fallback
  // when a detail URL is opened without an explicit source (Discover always sets one, back nav may not).
  return requestKindInfo(kind) ? REQUEST_PROVIDER_KIND.plugin : null;
}

export function numericValue(value: number | string | null | undefined): number | null {
  if (typeof value === "number") return value;
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
}
