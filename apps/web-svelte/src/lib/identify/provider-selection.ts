import { ENTITY_KIND_DEFINITIONS } from "$lib/api/generated/codes";
import type { PluginEntitySupport, PluginProvider } from "$lib/api/identify-types";
import { isEntityKindCode } from "$lib/entities/entity-codes";

/** Compares plugin IDs using the same case-insensitive policy as the backend catalog lookup. */
export function providerIdsEqual(
  providerId: string,
  configuredProviderId?: string | null,
): boolean {
  return Boolean(configuredProviderId) &&
    providerId.localeCompare(configuredProviderId ?? "", undefined, { sensitivity: "accent" }) === 0;
}

/**
 * Returns the support entry a provider identifies an entity kind through: its direct entry for the
 * kind when it declares one, otherwise its entry for the kind's definition-owned fallback (a
 * `video` provider identifying a movie). Search fields and actions come from this entry.
 */
export function providerSupportForKind(
  provider: Pick<PluginProvider, "supports">,
  kind: string,
): PluginEntitySupport | null {
  const normalizedKind = kind.toLowerCase();
  const direct = provider.supports.find((support) => support.entityKind.toLowerCase() === normalizedKind);
  if (direct) return direct;

  const fallbackKind = isEntityKindCode(normalizedKind)
    ? ENTITY_KIND_DEFINITIONS[normalizedKind].identifyPluginFallbackKind
    : null;
  return fallbackKind === null
    ? null
    : provider.supports.find((support) => support.entityKind.toLowerCase() === fallbackKind) ?? null;
}

/** Whether an installed provider is currently usable for an entity kind. */
export function providerCanIdentifyKind(provider: PluginProvider, kind: string): boolean {
  return provider.installed &&
    provider.enabled &&
    provider.missingAuthKeys.length === 0 &&
    providerSupportForKind(provider, kind) !== null;
}

/** Orders a usable provider list with its configured default first, then alphabetically by name. */
export function orderProvidersWithDefault(
  providers: PluginProvider[],
  defaultProviderId?: string | null,
): PluginProvider[] {
  return providers.toSorted((left, right) => {
    const leftIsDefault = providerIdsEqual(left.id, defaultProviderId);
    const rightIsDefault = providerIdsEqual(right.id, defaultProviderId);
    if (leftIsDefault !== rightIsDefault) return leftIsDefault ? -1 : 1;
    return left.name.localeCompare(right.name);
  });
}

/** Returns visible, usable Identify providers for a kind using the configured initial selection. */
export function selectIdentifyProviders(
  providers: PluginProvider[],
  kind: string,
  defaultProviderId: string | null | undefined,
  hideNsfw: boolean,
): PluginProvider[] {
  return orderProvidersWithDefault(
    providers.filter((provider) =>
      providerCanIdentifyKind(provider, kind) &&
      (!hideNsfw || !provider.isNsfw)
    ),
    defaultProviderId,
  );
}
