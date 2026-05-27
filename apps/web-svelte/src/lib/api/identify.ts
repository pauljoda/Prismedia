import { fetchApi, apiPath } from "$lib/api/orval-fetch";
import type { EntityCard, EntityDetailCard, EntityListResponse } from "$lib/api/entities";
import type { EntityMetadataFlagsPatch } from "$lib/api/entity-mutations";

export interface PluginEntitySupport {
  entityKind: string;
  actions: string[];
}

export interface PluginAuthField {
  key: string;
  label: string;
  required: boolean;
  url?: string | null;
}

export interface PluginProvider {
  id: string;
  name: string;
  version: string;
  installed: boolean;
  enabled: boolean;
  isNsfw: boolean;
  supports: PluginEntitySupport[];
  auth: PluginAuthField[];
  missingAuthKeys: string[];
}

export interface IdentifyQuery {
  title?: string | null;
  url?: string | null;
  externalIds?: Record<string, string> | null;
  requireChoice?: boolean | null;
}

export interface ImageCandidate {
  kind: string;
  url: string;
  source: string;
  rank?: number | null;
  language?: string | null;
  width?: number | null;
  height?: number | null;
}

export interface EntitySearchCandidate {
  externalIds: Record<string, string>;
  title: string;
  year?: number | null;
  overview?: string | null;
  posterUrl?: string | null;
  popularity?: number | null;
}

export interface CreditPatch {
  name: string;
  role: string;
  character?: string | null;
  sortOrder?: number | null;
}

export type { EntityMetadataFlagsPatch };

export interface EntityMetadataPatch {
  title?: string | null;
  description?: string | null;
  externalIds: Record<string, string>;
  urls: string[];
  tags: string[];
  studio?: string | null;
  credits: CreditPatch[];
  dates: Record<string, string>;
  stats: Record<string, number>;
  positions: Record<string, number>;
  classification?: string | null;
  flags?: EntityMetadataFlagsPatch | null;
}

export interface EntityMetadataProposal {
  proposalId: string;
  provider: string;
  targetKind: string;
  confidence?: number | null;
  matchReason?: string | null;
  patch: EntityMetadataPatch;
  images: ImageCandidate[];
  children: EntityMetadataProposal[];
  relationships: EntityMetadataProposal[];
  candidates: EntitySearchCandidate[];
  targetEntityId?: string | null;
}

export interface IdentifyBulkResult {
  entityId: string;
  response: {
    ok: boolean;
    result?: EntityMetadataProposal | null;
    error?: string | null;
  };
}

export interface IdentifyBulkSession {
  id: string;
  provider: string;
  entityIds: string[];
  results: IdentifyBulkResult[];
  status: "running" | "completed" | string;
  createdAt: string;
}

export type IdentifyQueueState = "search" | "proposal" | "done" | "deleted" | "error";

export interface IdentifyQueueItem {
  id: string;
  entityId: string;
  entityKind: string;
  title: string;
  isNsfw: boolean;
  state: IdentifyQueueState;
  provider?: string | null;
  action: string;
  query?: IdentifyQuery | null;
  candidates: EntitySearchCandidate[];
  proposal?: EntityMetadataProposal | null;
  error?: string | null;
  createdAt: string;
  updatedAt: string;
  completedAt?: string | null;
}


function query(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value != null && value !== "") search.set(key, String(value));
  }
  const qs = search.toString();
  return qs ? `?${qs}` : "";
}

export function fetchPluginProviders(): Promise<PluginProvider[]> {
  return fetchApi("/plugins");
}

export function installPlugin(provider: string): Promise<PluginProvider> {
  return fetchApi(`/plugins/${provider}`, { method: "POST" });
}

export function removePlugin(provider: string): Promise<void> {
  return fetchApi(`/plugins/${provider}`, { method: "DELETE" });
}

export function savePluginAuth(
  provider: string,
  values: Record<string, string | null>,
): Promise<void> {
  return fetchApi(`/plugins/${provider}/auth`, {
    method: "PUT",
    body: JSON.stringify({ values }),
  });
}

export function fetchIdentifyProviders(kind?: string): Promise<PluginProvider[]> {
  return fetchApi(`/identify/providers${query({ kind })}`);
}

export function providerCanIdentifyKind(provider: PluginProvider, kind: string): boolean {
  const normalizedKind = kind.toLowerCase();
  return provider.installed &&
    provider.enabled &&
    provider.missingAuthKeys.length === 0 &&
    provider.supports.some((support) => support.entityKind.toLowerCase() === normalizedKind);
}

export function identifyEntity(
  entityId: string,
  provider: string,
  identifyQuery?: IdentifyQuery,
): Promise<EntityMetadataProposal> {
  return fetchApi(`/identify/entities/${entityId}`, {
    method: "POST",
    body: JSON.stringify({ provider, query: identifyQuery ?? null }),
  });
}

export function fetchIdentifyEntity(entityId: string): Promise<EntityDetailCard> {
  return fetchApi(`/entities/${entityId}`);
}

export function applyIdentifyProposal(
  entityId: string,
  proposal: EntityMetadataProposal,
  selectedFields: string[],
  selectedImages?: Record<string, string | null>,
): Promise<EntityCard> {
  return fetchApi(`/identify/entities/${entityId}/apply`, {
    method: "POST",
    body: JSON.stringify({ proposal, selectedFields, selectedImages }),
  });
}

export function fetchIdentifyQueue(includeCompleted = false, hideNsfw?: boolean): Promise<IdentifyQueueItem[]> {
  return fetchApi(`/identify/queue${query({ includeCompleted, hideNsfw })}`);
}

export function addIdentifyQueueItem(entityId: string): Promise<IdentifyQueueItem> {
  return fetchApi(`/identify/queue/entities/${entityId}`, { method: "POST" });
}

export function fetchIdentifyQueueItem(entityId: string): Promise<IdentifyQueueItem> {
  return fetchApi(`/identify/queue/entities/${entityId}`);
}

export async function fetchOptionalIdentifyQueueItem(entityId: string): Promise<IdentifyQueueItem | null> {
  const response = await fetch(apiPath(`/identify/queue/entities/${entityId}`));
  if (response.status === 404) return null;
  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `API ${response.status}: ${response.statusText}`);
  }

  return (await response.json()) as IdentifyQueueItem;
}

export function searchIdentifyQueueItem(
  entityId: string,
  provider: string,
  identifyQuery?: IdentifyQuery,
): Promise<IdentifyQueueItem> {
  return fetchApi(`/identify/queue/entities/${entityId}/search`, {
    method: "POST",
    body: JSON.stringify({ provider, query: identifyQuery ?? null }),
  });
}

export function applyIdentifyQueueItem(
  entityId: string,
  proposal: EntityMetadataProposal | null,
  selectedFields: string[],
  selectedImages?: Record<string, string | null>,
): Promise<IdentifyQueueItem> {
  return fetchApi(`/identify/queue/entities/${entityId}/apply`, {
    method: "POST",
    body: JSON.stringify({ proposal, selectedFields, selectedImages }),
  });
}

export function deleteIdentifyQueueItem(entityId: string): Promise<IdentifyQueueItem> {
  return fetchApi(`/identify/queue/entities/${entityId}`, { method: "DELETE" });
}

export function startBulkIdentify(
  provider: string,
  entityIds: string[],
  identifyQuery?: IdentifyQuery,
): Promise<IdentifyBulkSession> {
  return fetchApi("/identify/bulk", {
    method: "POST",
    body: JSON.stringify({ provider, entityIds, query: identifyQuery ?? null }),
  });
}

export function fetchBulkIdentifySession(sessionId: string): Promise<IdentifyBulkSession> {
  return fetchApi(`/identify/bulk/${sessionId}`);
}

export function closeBulkIdentifySession(sessionId: string): Promise<void> {
  return fetchApi(`/identify/bulk/${sessionId}`, { method: "DELETE" });
}

export function fetchIdentifyEntities(
  kind: string,
  search?: string,
): Promise<EntityListResponse> {
  return fetchApi(`/entities${query({ kind, query: search })}`);
}
