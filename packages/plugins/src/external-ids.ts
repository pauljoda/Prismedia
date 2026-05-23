import type { ExternalIds } from "@prismedia/contracts";

/**
 * Read a provider value from an entity's external_ids column.
 * Returns undefined when the provider key is not present.
 */
export function getExternalId(
  externalIds: ExternalIds | null | undefined,
  provider: string,
): string | undefined {
  if (!externalIds) return undefined;
  const value = externalIds[provider];
  return typeof value === "string" ? value : undefined;
}

/**
 * Merge a provider value into an existing external_ids map, returning a
 * new object. Does not mutate the input. Does not clobber other provider
 * keys. Passing an empty or whitespace-only value removes the key.
 */
export function setExternalId(
  externalIds: ExternalIds | null | undefined,
  provider: string,
  value: string,
): ExternalIds {
  const base: ExternalIds = { ...(externalIds ?? {}) };
  const trimmed = value.trim();
  if (trimmed === "") {
    delete base[provider];
  } else {
    base[provider] = trimmed;
  }
  return base;
}

/**
 * Check whether a provider key is present on an entity.
 */
export function hasExternalId(
  externalIds: ExternalIds | null | undefined,
  provider: string,
): boolean {
  return getExternalId(externalIds, provider) !== undefined;
}

/**
 * Merge two external_ids maps. Keys in `incoming` win over keys in `base`.
 */
export function mergeExternalIds(
  base: ExternalIds | null | undefined,
  incoming: ExternalIds | null | undefined,
): ExternalIds {
  return { ...(base ?? {}), ...(incoming ?? {}) };
}
