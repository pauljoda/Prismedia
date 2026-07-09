import {
  addIdentifyQueueItem as addIdentifyQueueItemRequest,
  applyIdentifyProposal as applyIdentifyProposalRequest,
  identifyEntity as identifyEntityRequest,
  applyIdentifyQueueItem as applyIdentifyQueueItemRequest,
  deleteIdentifyQueueItem as deleteIdentifyQueueItemRequest,
  saveIdentifyQueueProposal as saveIdentifyQueueProposalRequest,
  getIdentifyQueueItem as getIdentifyQueueItemRequest,
  getGetIdentifyQueueItemUrl,
  listIdentifyProviders,
  listIdentifyQueue,
  searchIdentifyQueueItem as searchIdentifyQueueItemRequest,
  startBulkIdentify as startBulkIdentifyRequest,
} from "$lib/api/generated/prismedia";
import type {
  ApplyIdentifyProposalRequest,
  ApplyIdentifyQueueItemRequest,
  IdentifyEntityRequest,
  IdentifyBulkStartRequest,
  IdentifyQueueSearchRequest,
  IdentifyBulkAcceptedResponse,
  ListIdentifyQueueParams,
  SaveIdentifyQueueProposalRequest,
} from "$lib/api/generated/model";
import { problemMessage, requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";
import { apiPath, fetchApi } from "$lib/api/orval-fetch";
import { fetchEntities, fetchEntity, type EntityCard, type EntityDetailCard, type EntityListResponse } from "$lib/api/entities";
import type {
  IdentifyApplyProgress,
  EntityMetadataProposal,
  EntitySearchCandidate,
  IdentifyQuery,
  IdentifyQueueItem,
  PluginProvider,
} from "$lib/api/identify-types";

interface ApplyIdentifyQueueItemOptions extends RequestOptions {
  progressId?: string | null;
}

export function fetchIdentifyProviders(kind?: string, options?: RequestOptions): Promise<PluginProvider[]> {
  return listIdentifyProviders({ kind }, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to list identify providers") as PluginProvider[],
  );
}

export function providerCanIdentifyKind(provider: PluginProvider, kind: string): boolean {
  const normalizedKind = kind.toLowerCase();
  return provider.installed &&
    provider.enabled &&
    provider.missingAuthKeys.length === 0 &&
    provider.supports.some((support) => support.entityKind.toLowerCase() === normalizedKind);
}

export function applyIdentifyProposal(
  entityId: string,
  proposal: EntityMetadataProposal,
  selectedFields: string[],
  selectedImages?: Record<string, string | null>,
  options?: RequestOptions,
): Promise<EntityCard> {
  return applyIdentifyProposalRequest(entityId, {
    proposal,
    selectedFields,
    selectedImages: selectedImages ?? {},
  } as ApplyIdentifyProposalRequest, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to apply identify proposal", [200, 204]) as EntityCard,
  );
}

export function fetchIdentifyQueue(
  includeCompleted = false,
  hideNsfw?: boolean,
  options?: RequestOptions,
): Promise<IdentifyQueueItem[]> {
  const params: ListIdentifyQueueParams = { includeCompleted, hideNsfw };
  return listIdentifyQueue(params, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to list identify queue") as IdentifyQueueItem[],
  );
}

export function addIdentifyQueueItem(entityId: string, options?: RequestOptions): Promise<IdentifyQueueItem> {
  return addIdentifyQueueItemRequest(entityId, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to queue entity for identify") as IdentifyQueueItem,
  );
}

export function fetchIdentifyQueueItem(entityId: string, options?: RequestOptions): Promise<IdentifyQueueItem> {
  return getIdentifyQueueItemRequest(entityId, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to fetch identify queue item") as IdentifyQueueItem,
  );
}

export async function fetchOptionalIdentifyQueueItem(
  entityId: string,
  options?: RequestOptions,
): Promise<IdentifyQueueItem | null> {
  const response = await fetch(apiPath(getGetIdentifyQueueItemUrl(entityId)), requestInit(options));
  if (response.status === 404) return null;
  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `API ${response.status}: ${response.statusText}`);
  }

  return (await response.json()) as IdentifyQueueItem;
}

/**
 * Requests a provider search for a queue item. The search runs as a background identify-search
 * job on the server; the returned item is already in the queued state and reports progress
 * through its state code. Pass a null provider to let the server walk enabled providers.
 */
export function requestIdentifySearch(
  entityId: string,
  provider: string | null,
  identifyQuery?: IdentifyQuery,
  hideNsfw?: boolean,
  options?: RequestOptions,
): Promise<IdentifyQueueItem> {
  return searchIdentifyQueueItemRequest(entityId, {
    provider,
    query: identifyQuery ?? null,
  } as IdentifyQueueSearchRequest, { hideNsfw }, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to request identify search") as IdentifyQueueItem,
  );
}

/**
 * Resolves a selected candidate into the queue item's proposal. Unlike a fresh search request,
 * this does not enqueue an identify-search job or clear the current candidate results while the
 * provider ID lookup is running.
 */
export function resolveIdentifyQueueCandidate(
  entityId: string,
  provider: string,
  candidate: EntitySearchCandidate,
  hideNsfw?: boolean,
  options?: RequestOptions,
): Promise<IdentifyQueueItem> {
  const params = new URLSearchParams();
  if (hideNsfw !== undefined) params.set("hideNsfw", String(hideNsfw));
  const suffix = params.toString() ? `?${params}` : "";

  return fetchIdentifyQueueMutation(
    `/identify/queue/entities/${encodeURIComponent(entityId)}/candidate${suffix}`,
    {
      provider,
      candidate,
    },
    "Failed to resolve selected identify candidate",
    options,
  );
}

async function fetchIdentifyQueueMutation<T>(
  path: string,
  body: unknown,
  fallback: string,
  options?: RequestOptions,
): Promise<T> {
  const response = await fetch(apiPath(path), {
    ...requestInit(options),
    method: "POST",
    credentials: "same-origin",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  const text = await response.text();
  const data = parseJsonOrText(text);
  if (!response.ok) {
    throw new Error(problemMessage(data) ?? fallback);
  }

  return data as T;
}

function parseJsonOrText(text: string): unknown {
  if (!text) return undefined;
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

export function applyIdentifyQueueItem(
  entityId: string,
  proposal: EntityMetadataProposal | null,
  selectedFields: string[],
  selectedImages?: Record<string, string | null>,
  options?: ApplyIdentifyQueueItemOptions,
): Promise<IdentifyQueueItem> {
  const request = {
    proposal,
    selectedFields,
    selectedImages: selectedImages ?? {},
    ...(options?.progressId ? { progressId: options.progressId } : {}),
  } as ApplyIdentifyQueueItemRequest;

  return applyIdentifyQueueItemRequest(entityId, request, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to apply identify queue item") as IdentifyQueueItem,
  );
}

export async function fetchIdentifyApplyProgress(
  entityId: string,
  progressId: string,
  options?: RequestOptions,
): Promise<IdentifyApplyProgress> {
  return fetchApi<IdentifyApplyProgress>(
    `/identify/queue/entities/${entityId}/apply-progress/${progressId}`,
    requestInit(options),
  );
}

export function deleteIdentifyQueueItem(entityId: string, options?: RequestOptions): Promise<IdentifyQueueItem> {
  return deleteIdentifyQueueItemRequest(entityId, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to delete identify queue item") as IdentifyQueueItem,
  );
}

/**
 * Persists an in-progress identify proposal back onto the queued entity (without applying it), so
 * incrementally resolved children survive navigation and page refresh.
 */
export function saveIdentifyQueueProposal(
  entityId: string,
  proposal: EntityMetadataProposal,
  options?: RequestOptions,
): Promise<IdentifyQueueItem> {
  return saveIdentifyQueueProposalRequest(entityId, { proposal } as SaveIdentifyQueueProposalRequest, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to save identify proposal") as IdentifyQueueItem,
  );
}

export function fetchIdentifyEntity(entityId: string, options?: RequestOptions): Promise<EntityDetailCard> {
  return fetchEntity(entityId, options);
}

/**
 * Runs a single transient identify for one entity (no queue side effects). Used to identify a
 * structural child on demand: the result is an {@link EntityMetadataProposal} carrying either a
 * matched patch or search candidates. Pass `options.signal` to abort an in-flight lookup.
 */
export function identifyEntityTransient(
  entityId: string,
  provider: string,
  query?: IdentifyQuery | null,
  parentExternalIds?: Record<string, string> | null,
  options?: RequestOptions & { signal?: AbortSignal },
): Promise<EntityMetadataProposal> {
  return identifyEntityRequest(
    entityId,
    { provider, query: query ?? null, parentExternalIds: parentExternalIds ?? null } as IdentifyEntityRequest,
    undefined,
    { ...requestInit(options), signal: options?.signal },
  ).then((response) =>
    unwrapGenerated(response, "Failed to identify entity") as EntityMetadataProposal,
  );
}

export function fetchIdentifyEntities(
  kind: string,
  search?: string,
  options?: RequestOptions,
): Promise<EntityListResponse> {
  return fetchEntities({ kind, query: search, hasFile: true, wanted: false }, options);
}

export function startBulkIdentify(
  provider: string | null,
  entityIds: string[],
  query?: IdentifyQuery | null,
  hideNsfw?: boolean,
  options?: RequestOptions,
): Promise<IdentifyBulkAcceptedResponse> {
  return startBulkIdentifyRequest({
    provider,
    entityIds,
    query: query ?? null,
  } as IdentifyBulkStartRequest, { hideNsfw }, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to start bulk identify", [202]) as IdentifyBulkAcceptedResponse,
  );
}
