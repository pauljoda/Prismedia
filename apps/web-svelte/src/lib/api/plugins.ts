import type {
  CommunityIndexEntryDto,
  MetadataProviderDto,
  NormalizedPerformerResult,
  NormalizedSceneScrapeResultDto,
  NormalizedStudioScrapeResultDto,
  NormalizedTagScrapeResultDto,
  PluginIndexEntryDto,
  PluginPackageDto,
  ScrapeResultDto,
  ScraperPackageDto,
  StashBoxEndpointDto,
  StashBoxStudioResultDto,
  StashBoxTagResultDto,
  StashIdEntryDto,
} from "@prismedia/contracts";
import {
  installPlugin as installPluginProviderRequest,
  listPlugins,
  removePlugin as removePluginProviderRequest,
  updatePluginAuth,
} from "$lib/api/generated/prismedia";
import type { PluginAuthUpdateRequest } from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";
import { fetchApi } from "$lib/api/orval-fetch";
import type { PluginProvider } from "$lib/api/identify-types";

export type ScraperPackage = ScraperPackageDto;
export type CommunityIndexEntry = CommunityIndexEntryDto;
export type ScrapeResult = ScrapeResultDto;
export type NormalizedScrapeResult = NormalizedSceneScrapeResultDto;
export type NormalizedPerformerScrapeResult = NormalizedPerformerResult;
export type NormalizedStudioScrapeResult = NormalizedStudioScrapeResultDto;
export type NormalizedTagScrapeResult = NormalizedTagScrapeResultDto;
export type StashBoxEndpoint = StashBoxEndpointDto;
export type MetadataProvider = MetadataProviderDto;
export type StashIdEntry = StashIdEntryDto;
export type StashBoxStudioResult = StashBoxStudioResultDto;
export type StashBoxTagResult = StashBoxTagResultDto;
export type PrismediaPluginIndexEntry = PluginIndexEntryDto & {
  localPath?: string;
  updateAvailable?: boolean;
};
export type InstalledPlugin = PluginPackageDto;
export type { PluginProvider };

export interface PluginUpdateStatus {
  pluginId: string;
  installedVersion: string;
  availableVersion: string | null;
  updateAvailable: boolean;
  zipUrl: string | null;
  sha256: string | null;
}

export interface PluginExecuteResult {
  ok: boolean;
  result: unknown;
  normalized?: NormalizedScrapeResult;
  pluginId: string;
  action: string;
}

export function fetchPluginProviders(): Promise<PluginProvider[]> {
  return listPlugins().then((response) =>
    unwrapGenerated(response, "Failed to list plugin providers") as PluginProvider[],
  );
}

export function installPlugin(provider: string): Promise<PluginProvider> {
  return installPluginProviderRequest(provider).then((response) =>
    unwrapGenerated(response, "Failed to install plugin provider") as PluginProvider,
  );
}

export function updatePlugin(provider: string): Promise<PluginProvider> {
  return fetchApi(`/api/plugins/${encodeURIComponent(provider)}/update`, {
    method: "POST",
  });
}

export function removePlugin(provider: string): Promise<void> {
  return removePluginProviderRequest(provider).then((response) =>
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


function query(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value != null && value !== "") search.set(key, String(value));
  }
  const qs = search.toString();
  return qs ? `?${qs}` : "";
}

export function fetchCommunityIndex(
  force = false,
): Promise<{ entries: CommunityIndexEntry[] }> {
  return fetchApi(`/scrapers/index${force ? "?force=true" : ""}`);
}

export function fetchInstalledScrapers(): Promise<{ packages: ScraperPackage[] }> {
  return fetchApi("/scrapers/packages");
}

export function installScraper(packageId: string): Promise<ScraperPackage> {
  return fetchApi("/scrapers/packages", {
    method: "POST",
    body: JSON.stringify({ packageId }),
  });
}

export function uninstallScraper(id: string): Promise<{ ok: true }> {
  return fetchApi(`/scrapers/packages/${id}`, { method: "DELETE" });
}

export function toggleScraper(id: string, enabled: boolean): Promise<ScraperPackage> {
  return fetchApi(`/scrapers/packages/${id}`, {
    method: "PATCH",
    body: JSON.stringify({ enabled }),
  });
}

export function scrapeVideo(
  scraperId: string,
  videoId: string,
  action = "auto",
  options?: { url?: string; query?: string },
): Promise<{
  result?: ScrapeResult;
  normalized?: NormalizedScrapeResult;
  results?: NormalizedScrapeResult[];
  message?: string;
  action?: string;
  triedActions?: string[];
}> {
  return fetchApi(`/scrapers/${scraperId}/scrape`, {
    method: "POST",
    body: JSON.stringify({
      videoId,
      action,
      url: options?.url,
      query: options?.query,
    }),
  });
}

export function fetchScrapeResults(params?: {
  status?: string;
  videoId?: string;
  limit?: number;
  offset?: number;
}): Promise<{ results: ScrapeResult[]; total: number; limit: number; offset: number }> {
  return fetchApi(`/scrapers/results${query(params ?? {})}`);
}

const SCRAPE_RESULTS_PAGE_SIZE = 500;

export async function fetchAllScrapeResults(params?: {
  status?: string;
  videoId?: string;
}): Promise<{ results: ScrapeResult[]; total: number }> {
  const results: ScrapeResult[] = [];
  let offset = 0;
  let total = 0;

  for (;;) {
    const page = await fetchScrapeResults({
      ...params,
      limit: SCRAPE_RESULTS_PAGE_SIZE,
      offset,
    });
    total = page.total;
    results.push(...page.results);
    if (page.results.length < SCRAPE_RESULTS_PAGE_SIZE || results.length >= total) break;
    offset += SCRAPE_RESULTS_PAGE_SIZE;
  }

  return { results, total };
}

export function fetchScrapeResult(id: string): Promise<ScrapeResult> {
  return fetchApi(`/scrapers/results/${id}`);
}

export function acceptScrapeResult(
  id: string,
  fields?: string[],
  options?: { excludePerformers?: string[]; excludeTags?: string[] },
): Promise<{ ok: true; videoId: string }> {
  return fetchApi(`/scrapers/results/${id}/accept`, {
    method: "POST",
    body: JSON.stringify({
      fields,
      excludePerformers: options?.excludePerformers,
      excludeTags: options?.excludeTags,
    }),
  });
}

export function rejectScrapeResult(id: string): Promise<{ ok: true }> {
  return fetchApi(`/scrapers/results/${id}/reject`, { method: "POST" });
}

export function fetchStashBoxEndpoints(): Promise<{ endpoints: StashBoxEndpoint[] }> {
  return fetchApi("/stashbox-endpoints");
}

export function createStashBoxEndpoint(data: {
  name: string;
  endpoint: string;
  apiKey: string;
}): Promise<StashBoxEndpoint> {
  return fetchApi("/stashbox-endpoints", {
    method: "POST",
    body: JSON.stringify(data),
  });
}

export function updateStashBoxEndpoint(
  id: string,
  data: { name?: string; endpoint?: string; apiKey?: string; enabled?: boolean },
): Promise<StashBoxEndpoint> {
  return fetchApi(`/stashbox-endpoints/${id}`, {
    method: "PATCH",
    body: JSON.stringify(data),
  });
}

export function deleteStashBoxEndpoint(id: string): Promise<{ ok: true }> {
  return fetchApi(`/stashbox-endpoints/${id}`, { method: "DELETE" });
}

export function testStashBoxEndpoint(
  id: string,
): Promise<{ valid: boolean; error?: string }> {
  return fetchApi(`/stashbox-endpoints/${id}/test`, { method: "POST" });
}

export function identifyViaStashBox(
  endpointId: string,
  videoId: string,
): Promise<{
  result?: ScrapeResult;
  normalized?: NormalizedScrapeResult;
  matchType?: string;
  message?: string;
  triedMethods?: string[];
}> {
  return fetchApi(`/stashbox-endpoints/${endpointId}/identify`, {
    method: "POST",
    body: JSON.stringify({ videoId }),
  });
}

export function identifyPerformerViaStashBox(
  endpointId: string,
  performerId: string,
): Promise<{
  results?: NormalizedPerformerScrapeResult[];
  result?: null;
  message?: string;
}> {
  return fetchApi(`/stashbox-endpoints/${endpointId}/identify-performer`, {
    method: "POST",
    body: JSON.stringify({ performerId }),
  });
}

export function fetchMetadataProviders(): Promise<{ providers: MetadataProvider[] }> {
  return fetchApi("/metadata-providers");
}

export function fetchStashIds(
  entityType: string,
  entityId: string,
): Promise<{ stashIds: StashIdEntry[] }> {
  return fetchApi(`/stash-ids${query({ entityType, entityId })}`);
}

export function createStashId(data: {
  entityType: string;
  entityId: string;
  stashBoxEndpointId: string;
  stashId: string;
}): Promise<StashIdEntry> {
  return fetchApi("/stash-ids", {
    method: "POST",
    body: JSON.stringify(data),
  });
}

export function deleteStashId(id: string): Promise<{ ok: true }> {
  return fetchApi(`/stash-ids/${id}`, { method: "DELETE" });
}

export function lookupStudioViaStashBox(
  endpointId: string,
  lookupQuery: string,
): Promise<{ studio: StashBoxStudioResult | null }> {
  return fetchApi(`/stashbox-endpoints/${endpointId}/lookup/studio`, {
    method: "POST",
    body: JSON.stringify({ query: lookupQuery }),
  });
}

export function lookupTagViaStashBox(
  endpointId: string,
  lookupQuery: string,
): Promise<{ tags: StashBoxTagResult[] }> {
  return fetchApi(`/stashbox-endpoints/${endpointId}/lookup/tag`, {
    method: "POST",
    body: JSON.stringify({ query: lookupQuery }),
  });
}

export function lookupPerformerViaStashBox(
  endpointId: string,
  lookupQuery: string,
): Promise<{
  performers: NormalizedPerformerScrapeResult[];
  rawPerformers: unknown[];
}> {
  return fetchApi(`/stashbox-endpoints/${endpointId}/lookup/performer`, {
    method: "POST",
    body: JSON.stringify({ query: lookupQuery }),
  });
}

export function fetchPrismediaPluginIndex(
  options: { refresh?: boolean } = {},
): Promise<PrismediaPluginIndexEntry[]> {
  return fetchApi(`/plugins/prismedia-index${options.refresh ? "?refresh=1" : ""}`);
}

export function fetchPluginUpdates(
  options: { refresh?: boolean } = {},
): Promise<PluginUpdateStatus[]> {
  return fetchApi(`/plugins/check-updates${options.refresh ? "?refresh=1" : ""}`);
}

export function installPrismediaPlugin(
  pluginId: string,
  options: { localPath?: string; zipUrl?: string; sha256?: string },
): Promise<{ ok: boolean; pluginId: string }> {
  return fetchApi("/plugins/packages", {
    method: "POST",
    body: JSON.stringify({ pluginId, ...options }),
  });
}

export function fetchInstalledPlugins(): Promise<InstalledPlugin[]> {
  return fetchApi("/plugins/packages");
}

export function togglePlugin(id: string, enabled: boolean): Promise<{ ok: boolean }> {
  return fetchApi(`/plugins/packages/${id}`, {
    method: "PATCH",
    body: JSON.stringify({ enabled }),
  });
}

export function uninstallPlugin(id: string): Promise<{ ok: boolean }> {
  return fetchApi(`/plugins/packages/${id}`, { method: "DELETE" });
}

export function acceptPluginResult(
  resultId: string,
  fields?: string[],
  selectedImages?: Record<string, string | null | undefined>,
): Promise<{ ok: boolean }> {
  return fetchApi(`/plugins/results/${resultId}/accept`, {
    method: "POST",
    body: JSON.stringify({ fields, selectedImages }),
  });
}

export function executePlugin(
  pluginDbId: string,
  action: string,
  input?: Record<string, unknown>,
  options?: { saveResult?: boolean; entityId?: string },
): Promise<PluginExecuteResult> {
  return fetchApi(`/plugins/${pluginDbId}/execute`, {
    method: "POST",
    body: JSON.stringify({ action, input, ...options }),
  });
}

export function savePluginAuthKey(
  pluginDbId: string,
  authKey: string,
  value: string,
): Promise<{ ok: boolean }> {
  return fetchApi(`/plugins/packages/${pluginDbId}/auth/${authKey}`, {
    method: "PUT",
    body: JSON.stringify({ value }),
  });
}
