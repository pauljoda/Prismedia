import { apiPath } from "$lib/api/orval-fetch";
import type { EntityCard, EntityDetailCard, EntityListResponse } from "$lib/api/prismedia";

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
  supports: PluginEntitySupport[];
  auth: PluginAuthField[];
  missingAuthKeys: string[];
}

export interface IdentifyQuery {
  title?: string | null;
  url?: string | null;
  externalIds?: Record<string, string> | null;
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

async function apiJson<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers);
  if (init?.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(apiPath(path), { ...init, headers });
  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `API ${response.status}: ${response.statusText}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
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
  return apiJson("/plugins");
}

export function installPlugin(provider: string): Promise<PluginProvider> {
  return apiJson(`/plugins/${provider}`, { method: "POST" });
}

export function removePlugin(provider: string): Promise<void> {
  return apiJson(`/plugins/${provider}`, { method: "DELETE" });
}

export function savePluginAuth(
  provider: string,
  values: Record<string, string | null>,
): Promise<void> {
  return apiJson(`/plugins/${provider}/auth`, {
    method: "PUT",
    body: JSON.stringify({ values }),
  });
}

export function fetchIdentifyProviders(kind?: string): Promise<PluginProvider[]> {
  return apiJson(`/identify/providers${query({ kind })}`);
}

export function identifyEntity(
  entityId: string,
  provider: string,
  identifyQuery?: IdentifyQuery,
): Promise<EntityMetadataProposal> {
  return apiJson(`/identify/entities/${entityId}`, {
    method: "POST",
    body: JSON.stringify({ provider, query: identifyQuery ?? null }),
  });
}

export function fetchIdentifyEntity(entityId: string): Promise<EntityDetailCard> {
  return apiJson(`/entities/${entityId}`);
}

export function applyIdentifyProposal(
  entityId: string,
  proposal: EntityMetadataProposal,
  selectedFields: string[],
  selectedImages?: Record<string, string | null>,
): Promise<EntityCard> {
  return apiJson(`/identify/entities/${entityId}/apply`, {
    method: "POST",
    body: JSON.stringify({ proposal, selectedFields, selectedImages }),
  });
}

export function startBulkIdentify(
  provider: string,
  entityIds: string[],
  identifyQuery?: IdentifyQuery,
): Promise<IdentifyBulkSession> {
  return apiJson("/identify/bulk", {
    method: "POST",
    body: JSON.stringify({ provider, entityIds, query: identifyQuery ?? null }),
  });
}

export function fetchBulkIdentifySession(sessionId: string): Promise<IdentifyBulkSession> {
  return apiJson(`/identify/bulk/${sessionId}`);
}

export function closeBulkIdentifySession(sessionId: string): Promise<void> {
  return apiJson(`/identify/bulk/${sessionId}`, { method: "DELETE" });
}

export function fetchIdentifyEntities(
  kind: string,
  search?: string,
): Promise<EntityListResponse> {
  return apiJson(`/entities${query({ kind, query: search })}`);
}
