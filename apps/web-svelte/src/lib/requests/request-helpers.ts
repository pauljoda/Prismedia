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
  /**
   * The library entity kind this request materializes as. Request items are "virtual" entities: they
   * render through the exact same components — and therefore the same thumbnail shape, placeholder
   * family, and detail layout — as the real thing they become (a movie result IS a movie poster, an
   * album result IS square album art).
   */
  entityKind: EntityKindCode;
  /** Entity kind used at the plugin protocol boundary; authors identify as people before materializing as book-author entities. */
  pluginEntityKind: EntityKindCode;
  /** The acquisition-profile kind this request's downloads are governed by (null while not committable). */
  profileKind: EntityKindCode | null;
  /** The library-root capability flag the acquired files need ("scanBooks" | "scanVideos" | "scanAudio"). */
  rootFlag: "scanBooks" | "scanVideos" | "scanAudio" | null;
  /** Whether Discover offers the kind directly; unit kinds (season, episode) exist only inside their parent's flow. */
  discoverable: boolean;
  /** How review maps a plugin proposal to the root-or-direct-child commit contract. */
  reviewSelection: "root" | "direct-children" | "direct-children-when-present";
}

export const REQUEST_KINDS: RequestKindInfo[] = [
  { kind: REQUEST_MEDIA_KIND.book, label: "Book", plural: "Books", committable: true, childNoun: "volume", entityKind: ENTITY_KIND.book, pluginEntityKind: ENTITY_KIND.book, profileKind: ENTITY_KIND.book, rootFlag: "scanBooks", discoverable: true, reviewSelection: "direct-children-when-present" },
  { kind: REQUEST_MEDIA_KIND.author, label: "Author", plural: "Authors", committable: true, childNoun: "book", entityKind: ENTITY_KIND.bookAuthor, pluginEntityKind: ENTITY_KIND.person, profileKind: ENTITY_KIND.book, rootFlag: "scanBooks", discoverable: true, reviewSelection: "direct-children" },
  { kind: REQUEST_MEDIA_KIND.movie, label: "Movie", plural: "Movies", committable: true, childNoun: null, entityKind: ENTITY_KIND.movie, pluginEntityKind: ENTITY_KIND.movie, profileKind: ENTITY_KIND.movie, rootFlag: "scanVideos", discoverable: true, reviewSelection: "root" },
  { kind: REQUEST_MEDIA_KIND.series, label: "Series", plural: "Series", committable: true, childNoun: "season", entityKind: ENTITY_KIND.videoSeries, pluginEntityKind: ENTITY_KIND.videoSeries, profileKind: ENTITY_KIND.videoSeries, rootFlag: "scanVideos", discoverable: true, reviewSelection: "direct-children" },
  { kind: REQUEST_MEDIA_KIND.season, label: "Season", plural: "Seasons", committable: true, childNoun: "episode", entityKind: ENTITY_KIND.videoSeason, pluginEntityKind: ENTITY_KIND.videoSeason, profileKind: ENTITY_KIND.videoSeries, rootFlag: "scanVideos", discoverable: false, reviewSelection: "root" },
  { kind: REQUEST_MEDIA_KIND.episode, label: "Episode", plural: "Episodes", committable: true, childNoun: null, entityKind: ENTITY_KIND.video, pluginEntityKind: ENTITY_KIND.video, profileKind: ENTITY_KIND.videoSeries, rootFlag: "scanVideos", discoverable: false, reviewSelection: "root" },
  { kind: REQUEST_MEDIA_KIND.artist, label: "Artist", plural: "Artists", committable: true, childNoun: "album", entityKind: ENTITY_KIND.musicArtist, pluginEntityKind: ENTITY_KIND.musicArtist, profileKind: ENTITY_KIND.audioLibrary, rootFlag: "scanAudio", discoverable: true, reviewSelection: "direct-children" },
  { kind: REQUEST_MEDIA_KIND.album, label: "Album", plural: "Albums", committable: true, childNoun: null, entityKind: ENTITY_KIND.audioLibrary, pluginEntityKind: ENTITY_KIND.audioLibrary, profileKind: ENTITY_KIND.audioLibrary, rootFlag: "scanAudio", discoverable: true, reviewSelection: "root" },
];

/** The kinds Discover's search and its kind chips offer. */
export const DISCOVERABLE_REQUEST_KINDS: RequestKindInfo[] = REQUEST_KINDS.filter((info) => info.discoverable);

/**
 * The library entity kind represented by a request media kind. Unmapped kinds fall back to book.
 */
export function entityKindForRequest(kind: RequestMediaKindCode | string): EntityKindCode {
  return requestKindInfo(kind as RequestMediaKindCode)?.entityKind ?? ENTITY_KIND.book;
}

/** The request media kind a library entity kind maps back to (for queue grouping), or null when none does. */
export function requestKindForEntityKind(entityKind: string): RequestMediaKindCode | null {
  return REQUEST_KINDS.find((info) => info.entityKind === entityKind)?.kind ?? null;
}

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
