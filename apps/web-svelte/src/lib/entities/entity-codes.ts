import {
  CAPABILITY_KIND,
  CREDIT_ROLE,
  ENTITY_FILE_ROLE,
  ENTITY_KIND,
  ENTITY_KIND_LABELS,
  PLAYBACK_EVENT_KIND,
  PROPOSAL_KIND,
  RELATIONSHIP_CODE,
  type CapabilityKindCode,
  type CreditRoleCode,
  type EntityFileRoleCode,
  type EntityKindCode,
  type PlaybackEventKindCode,
  type ProposalKindCode,
  type RelationshipCode,
} from "$lib/api/generated/codes";

// Stable code constants are generated from the backend registries (see
// scripts/gen-codes.mjs). This module re-exports them and owns only the frontend-specific
// concerns: the kind label lookup and the route resolution rules.
export {
  CAPABILITY_KIND,
  CREDIT_ROLE,
  ENTITY_FILE_ROLE,
  ENTITY_KIND,
  ENTITY_KIND_LABELS,
  PLAYBACK_EVENT_KIND,
  PROPOSAL_KIND,
  RELATIONSHIP_CODE,
};
export type {
  CapabilityKindCode,
  CreditRoleCode,
  EntityFileRoleCode,
  EntityKindCode,
  PlaybackEventKindCode,
  ProposalKindCode,
  RelationshipCode,
};

/**
 * Maps a plugin-proposal kind to the entity kind Prismedia persists it as. Every proposal kind
 * shares its code with an entity kind except `video-episode` (a provider-only leaf-episode token),
 * which Prismedia stores as a `video`. Use this wherever a proposal's `targetKind` feeds an
 * entity-kind–typed surface such as a thumbnail card.
 */
export function proposalKindToEntityKind(kind: ProposalKindCode): EntityKindCode {
  return kind === PROPOSAL_KIND.videoEpisode ? ENTITY_KIND.video : (kind as EntityKindCode);
}

export const ENTITY_KINDS = Object.values(ENTITY_KIND) as EntityKindCode[];

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
  { kind: ENTITY_KIND.movie, topLevel: true, browsePath: "/movies", resolve: (id) => `/movies/${id}` },
  { kind: ENTITY_KIND.videoSeries, topLevel: true, browsePath: "/series", resolve: (id) => `/series/${id}` },
  { kind: ENTITY_KIND.videoSeason, topLevel: false, browsePath: "/series", resolve: (id, parent) => parent ? `/series/${parent.id}/seasons/${id}` : undefined },
  { kind: ENTITY_KIND.gallery, topLevel: true, browsePath: "/galleries", resolve: (id) => `/galleries/${id}` },
  { kind: ENTITY_KIND.book, topLevel: true, browsePath: "/books", resolve: (id) => `/books/${id}` },
  { kind: ENTITY_KIND.bookAuthor, topLevel: true, browsePath: "/authors", resolve: (id) => `/authors/${id}` },
  { kind: ENTITY_KIND.bookVolume, topLevel: false, browsePath: "/books", resolve: (id, parent) => parent ? `/books/${parent.id}/volumes/${id}` : undefined },
  { kind: ENTITY_KIND.bookChapter, topLevel: false, browsePath: "/books", resolve: (id, parent) => parent ? `/books/${parent.id}/chapters/${id}` : undefined },
  { kind: ENTITY_KIND.image, topLevel: true, browsePath: "/images", resolve: (id) => `/images/${id}` },
  { kind: ENTITY_KIND.musicArtist, topLevel: true, browsePath: "/artists", resolve: (id) => `/artists/${id}` },
  { kind: ENTITY_KIND.audioLibrary, topLevel: true, browsePath: "/audio", resolve: (id) => `/audio/${id}` },
  { kind: ENTITY_KIND.audioTrack, topLevel: false, browsePath: "/audio", resolve: (id) => `/audio/tracks/${id}` },
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
