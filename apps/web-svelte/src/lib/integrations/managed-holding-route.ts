import type { ManagedItemInput, ManagedLibraryItem, ManagedLibraryItemExternalIds } from "$lib/api/generated/model";
import { requestKindForEntityKind } from "$lib/requests/request-helpers";

const EXTERNAL_IDENTITIES_PARAMETER = "identities";
const MAX_IDENTITY_COUNT = 64;
const MAX_IDENTITY_KEY_LENGTH = 128;
const MAX_IDENTITY_VALUE_LENGTH = 2048;

/**
 * Builds the internal address for a connected-library holding. The external identities travel
 * with the link so a later lookup remains pinned to the title that was selected in the manager.
 */
export function managedHoldingHref(connectionId: string, item: ManagedLibraryItem): string {
  return managedHoldingIdentityHref(connectionId, item.entityKind, item.remoteId, item.externalIds);
}

/** Builds the same exact holding address from an identity pin saved with a library Entity. */
export function managedHoldingInputHref(connectionId: string, item: ManagedItemInput): string {
  const href = managedHoldingIdentityHref(connectionId, item.entityKind, item.remoteId, item.expectedExternalIds);
  if (!item.bookRendition) return href;
  const url = new URL(href, "http://localhost");
  url.searchParams.set("rendition", item.bookRendition);
  return url.pathname + url.search;
}

function managedHoldingIdentityHref(
  connectionId: string,
  entityKind: string,
  remoteId: string,
  externalIds: ManagedLibraryItemExternalIds,
): string {
  const identities = Object.fromEntries(
    Object.entries(externalIds).sort(([left], [right]) => left.localeCompare(right)),
  );
  const query = new URLSearchParams({ [EXTERNAL_IDENTITIES_PARAMETER]: JSON.stringify(identities) });

  return `/request/source/${encodeURIComponent(connectionId)}/${encodeURIComponent(entityKind)}/${encodeURIComponent(remoteId)}?${query}`;
}

/** Builds the Request workspace address that restores the source and matching media kind. */
export function managedHoldingSourceHref(connectionId: string, entityKind: string): string {
  const query = new URLSearchParams({ connection: connectionId });
  const requestKind = requestKindForEntityKind(entityKind);
  if (requestKind) query.set("kind", requestKind);
  return `/request?${query}`;
}

/**
 * Reads the identity pin embedded by {@link managedHoldingHref}. Invalid input stays invalid
 * instead of falling back to a remote ID, because manager IDs can be reused over time.
 */
export function parseManagedHoldingIdentities(value: string | null): ManagedLibraryItemExternalIds | null {
  if (value === null) return null;

  try {
    const parsed: unknown = JSON.parse(value);
    if (!parsed || Array.isArray(parsed) || typeof parsed !== "object") return null;

    const entries = Object.entries(parsed);
    if (entries.length === 0 || entries.length > MAX_IDENTITY_COUNT) return null;
    if (entries.some(([key, identity]) => !isValidIdentityPart(key, MAX_IDENTITY_KEY_LENGTH)
      || typeof identity !== "string"
      || !isValidIdentityPart(identity, MAX_IDENTITY_VALUE_LENGTH))) return null;

    return Object.fromEntries(entries) as ManagedLibraryItemExternalIds;
  } catch {
    return null;
  }
}

function isValidIdentityPart(value: string, maxLength: number): boolean {
  return value.length <= maxLength && value.trim().length > 0 && !Array.from(value).some(isControlCharacter);
}

function isControlCharacter(character: string): boolean {
  const codePoint = character.codePointAt(0);
  return codePoint != null && (codePoint <= 0x1f || codePoint === 0x7f);
}
