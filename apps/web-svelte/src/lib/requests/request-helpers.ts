import { ENTITY_KIND, REQUEST_KIND_MANIFEST } from "$lib/api/generated/codes";
import type {
  EntityKindCode,
  RequestKindManifestEntry,
  RequestMediaKindCode,
} from "$lib/api/generated/codes";

/**
 * Generated UI projection of the backend's RequestKindRegistry. It carries every per-kind label and
 * flow hint used by Discover, proposal review, and acquisition target selection.
 */
export type RequestKindInfo = RequestKindManifestEntry;

export const REQUEST_KINDS: readonly RequestKindInfo[] = REQUEST_KIND_MANIFEST;

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

export function numericValue(value: number | string | null | undefined): number | null {
  if (typeof value === "number") return value;
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
}
