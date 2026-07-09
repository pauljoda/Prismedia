import {
  installPlugin as installPluginRequest,
  listPlugins,
  listStashScrapers,
  removePlugin as removePluginRequest,
  updatePlugin as updatePluginRequest,
  updatePluginAuth,
} from "$lib/api/generated/prismedia";
import type {
  PluginAuthUpdateRequest,
  PluginProvider,
  StashScraperListing,
} from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";

export type { PluginProvider, StashScraperListing };

export function fetchPluginProviders(): Promise<PluginProvider[]> {
  return listPlugins().then((response) =>
    unwrapGenerated(response, "Failed to list plugin providers"),
  );
}

export function fetchStashScrapers(): Promise<StashScraperListing[]> {
  return listStashScrapers().then((response) =>
    unwrapGenerated(response, "Failed to list Stash community scrapers"),
  );
}

export function installPlugin(provider: string): Promise<PluginProvider> {
  return installPluginRequest(provider).then((response) =>
    unwrapGenerated(response, "Failed to install plugin provider"),
  );
}

export function updatePlugin(provider: string): Promise<PluginProvider> {
  return updatePluginRequest(provider).then((response) =>
    unwrapGenerated(response, "Failed to update plugin provider"),
  );
}

export function removePlugin(provider: string): Promise<void> {
  return removePluginRequest(provider).then((response) =>
    unwrapGenerated(response, "Failed to remove plugin provider", [204]),
  );
}

export function savePluginAuth(
  provider: string,
  values: Record<string, string | null>,
): Promise<void> {
  return updatePluginAuth(provider, { values } as PluginAuthUpdateRequest).then((response) =>
    unwrapGenerated(response, "Failed to save plugin auth", [204]),
  );
}
