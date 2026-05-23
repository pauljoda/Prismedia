import type { EntityCapability } from "$lib/api/generated/model";

/**  entity kind codes emitted by the .NET entity registry. */
export const ENTITY_KIND = {
  audio: "audio",
  audioLibrary: "audio-library",
  audioTrack: "audio-track",
  book: "book",
  bookChapter: "book-chapter",
  bookPage: "book-page",
  bookVolume: "book-volume",
  collection: "collection",
  gallery: "gallery",
  image: "image",
  person: "person",
  studio: "studio",
  tag: "tag",
  video: "video",
  videoSeason: "video-season",
  videoSeries: "video-series",
} as const;

export type EntityKindCode = (typeof ENTITY_KIND)[keyof typeof ENTITY_KIND];

export const ENTITY_KINDS = Object.values(ENTITY_KIND) as EntityKindCode[];

/**  capability discriminator codes from the generated OpenAPI union. */
export const CAPABILITY_KIND = {
  classification: "classification",
  dates: "dates",
  description: "description",
  files: "files",
  fingerprints: "fingerprints",
  flags: "flags",
  images: "images",
  links: "links",
  lifetime: "lifetime",
  markers: "markers",
  playback: "playback",
  position: "position",
  progress: "progress",
  rating: "rating",
  source: "source",
  stats: "stats",
  subtitles: "subtitles",
  technical: "technical",
} as const satisfies Record<string, EntityCapability["kind"]>;

export type CapabilityKindCode = (typeof CAPABILITY_KIND)[keyof typeof CAPABILITY_KIND];

export const RELATIONSHIP_CODE = {
  cast: "cast",
  studio: "studio",
  tags: "tags",
} as const;

export type RelationshipCode = (typeof RELATIONSHIP_CODE)[keyof typeof RELATIONSHIP_CODE];

/**  entity file/image role codes used by shared thumbnail and detail surfaces. */
export const ENTITY_FILE_ROLE = {
  banner: "banner",
  backdrop: "backdrop",
  cover: "cover",
  full: "full",
  hero: "hero",
  logo: "logo",
  original: "original",
  poster: "poster",
  preview: "preview",
  source: "source",
  sprite: "sprite",
  thumbnail: "thumbnail",
  trickplay: "trickplay",
} as const;

export type EntityFileRoleCode = (typeof ENTITY_FILE_ROLE)[keyof typeof ENTITY_FILE_ROLE];

const ENTITY_KIND_LABELS: Record<EntityKindCode, string> = {
  [ENTITY_KIND.audio]: "Audio",
  [ENTITY_KIND.audioLibrary]: "Audio Libraries",
  [ENTITY_KIND.audioTrack]: "Audio Tracks",
  [ENTITY_KIND.book]: "Books",
  [ENTITY_KIND.bookChapter]: "Book Chapters",
  [ENTITY_KIND.bookPage]: "Book Pages",
  [ENTITY_KIND.bookVolume]: "Book Volumes",
  [ENTITY_KIND.collection]: "Collections",
  [ENTITY_KIND.gallery]: "Galleries",
  [ENTITY_KIND.image]: "Images",
  [ENTITY_KIND.person]: "People",
  [ENTITY_KIND.studio]: "Studios",
  [ENTITY_KIND.tag]: "Tags",
  [ENTITY_KIND.video]: "Videos",
  [ENTITY_KIND.videoSeason]: "Seasons",
  [ENTITY_KIND.videoSeries]: "Series",
};

export interface EntityRouteContext {
  kind: EntityKindCode;
  id: string;
}

interface EntityRouteRule {
  kind: EntityKindCode;
  topLevel: boolean;
  browsePath: string;
  resolve(id: string, parent?: EntityRouteContext): string | undefined;
}

const ROUTE_RULES: EntityRouteRule[] = [
  { kind: ENTITY_KIND.video, topLevel: true, browsePath: "/videos", resolve: (id) => `/videos/${id}` },
  { kind: ENTITY_KIND.videoSeries, topLevel: true, browsePath: "/series", resolve: (id) => `/series/${id}` },
  { kind: ENTITY_KIND.videoSeason, topLevel: false, browsePath: "/series", resolve: (id, parent) => parent ? `/series/${parent.id}/seasons/${id}` : undefined },
  { kind: ENTITY_KIND.gallery, topLevel: true, browsePath: "/galleries", resolve: (id) => `/galleries/${id}` },
  { kind: ENTITY_KIND.book, topLevel: true, browsePath: "/books", resolve: (id) => `/books/${id}` },
  { kind: ENTITY_KIND.bookVolume, topLevel: false, browsePath: "/books", resolve: (id, parent) => parent ? `/books/${parent.id}/volumes/${id}` : undefined },
  { kind: ENTITY_KIND.bookChapter, topLevel: false, browsePath: "/books", resolve: (id, parent) => parent ? `/books/${parent.id}/chapters/${id}` : undefined },
  { kind: ENTITY_KIND.image, topLevel: true, browsePath: "/images", resolve: (id) => `/images/${id}` },
  { kind: ENTITY_KIND.audioLibrary, topLevel: true, browsePath: "/audio", resolve: (id) => `/audio/${id}` },
  { kind: ENTITY_KIND.audioTrack, topLevel: false, browsePath: "/audio", resolve: (id, parent) => parent ? `/audio/${parent.id}/tracks/${id}` : undefined },
  { kind: ENTITY_KIND.person, topLevel: true, browsePath: "/people", resolve: (id) => `/people/${id}` },
  { kind: ENTITY_KIND.studio, topLevel: true, browsePath: "/studios", resolve: (id) => `/studios/${id}` },
  { kind: ENTITY_KIND.tag, topLevel: true, browsePath: "/tags", resolve: (id) => `/tags/${id}` },
  { kind: ENTITY_KIND.collection, topLevel: true, browsePath: "/collections", resolve: (id) => `/collections/${id}` },
];

const routeRuleByKind = new Map<EntityKindCode, EntityRouteRule>(
  ROUTE_RULES.map((rule) => [rule.kind, rule]),
);

export function isEntityKindCode(value: string): value is EntityKindCode {
  return (ENTITY_KINDS as readonly string[]).includes(value);
}

export function labelForEntityKind(kind: string): string {
  if (isEntityKindCode(kind)) return ENTITY_KIND_LABELS[kind];
  return kind.replaceAll("-", " ").replace(/\b\w/g, (value) => value.toUpperCase());
}

export function resolveEntityHref(
  kind: string,
  id: string,
  parent?: EntityRouteContext,
): string | undefined {
  if (!isEntityKindCode(kind)) return undefined;
  return routeRuleByKind.get(kind)?.resolve(id, parent);
}

export function resolveEntityBrowsePath(kind: string): string | undefined {
  return isEntityKindCode(kind) ? routeRuleByKind.get(kind)?.browsePath : undefined;
}

export function isTopLevelEntityKind(kind: string): kind is EntityKindCode {
  return isEntityKindCode(kind) && (routeRuleByKind.get(kind)?.topLevel ?? false);
}
