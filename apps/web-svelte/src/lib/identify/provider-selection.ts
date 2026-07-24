import { ENTITY_KIND } from "$lib/api/generated/codes";
import type { PluginProvider } from "$lib/api/identify-types";

/** Compares plugin IDs using the same case-insensitive policy as the backend catalog lookup. */
export function providerIdsEqual(
  providerId: string,
  configuredProviderId?: string | null,
): boolean {
  return Boolean(configuredProviderId) &&
    providerId.localeCompare(configuredProviderId ?? "", undefined, { sensitivity: "accent" }) === 0;
}

/** Whether an installed provider is currently usable for an entity kind. */
export function providerCanIdentifyKind(provider: PluginProvider, kind: string): boolean {
  const normalizedKind = kind.toLowerCase();
  return provider.installed &&
    provider.enabled &&
    provider.missingAuthKeys.length === 0 &&
    provider.supports.some((support) => {
      const supportedKind = support.entityKind.toLowerCase();
      return supportedKind === normalizedKind ||
        normalizedKind === ENTITY_KIND.movie && supportedKind === ENTITY_KIND.video;
    });
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
