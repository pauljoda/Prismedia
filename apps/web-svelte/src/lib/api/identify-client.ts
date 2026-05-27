import {
  addIdentifyQueueItem as addIdentifyQueueItemRequest,
  applyIdentifyProposal as applyIdentifyProposalRequest,
  applyIdentifyQueueItem as applyIdentifyQueueItemRequest,
  deleteIdentifyQueueItem as deleteIdentifyQueueItemRequest,
  getIdentifyQueueItem as getIdentifyQueueItemRequest,
  getGetIdentifyQueueItemUrl,
  listIdentifyProviders,
  listIdentifyQueue,
  searchIdentifyQueueItem as searchIdentifyQueueItemRequest,
} from "$lib/api/generated/prismedia";
import type {
  ApplyIdentifyProposalRequest,
  ApplyIdentifyQueueItemRequest,
  IdentifyQueueSearchRequest,
  ListIdentifyQueueParams,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";
import { apiPath } from "$lib/api/orval-fetch";
import { fetchEntities, fetchEntity, type EntityCard, type EntityDetailCard, type EntityListResponse } from "$lib/api/entities";
import type {
  EntityMetadataProposal,
  IdentifyQuery,
  IdentifyQueueItem,
  PluginProvider,
} from "$lib/api/identify-types";

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

export function searchIdentifyQueueItem(
  entityId: string,
  provider: string,
  identifyQuery?: IdentifyQuery,
  options?: RequestOptions,
): Promise<IdentifyQueueItem> {
  return searchIdentifyQueueItemRequest(entityId, {
    provider,
    query: identifyQuery ?? null,
  } as IdentifyQueueSearchRequest, undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to search identify provider") as IdentifyQueueItem,
  );
}

export function applyIdentifyQueueItem(
  entityId: string,
  proposal: EntityMetadataProposal | null,
  selectedFields: string[],
  selectedImages?: Record<string, string | null>,
  options?: RequestOptions,
): Promise<IdentifyQueueItem> {
  return applyIdentifyQueueItemRequest(entityId, {
    proposal,
    selectedFields,
    selectedImages: selectedImages ?? {},
  } as ApplyIdentifyQueueItemRequest, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to apply identify queue item") as IdentifyQueueItem,
  );
}

export function deleteIdentifyQueueItem(entityId: string, options?: RequestOptions): Promise<IdentifyQueueItem> {
  return deleteIdentifyQueueItemRequest(entityId, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to delete identify queue item") as IdentifyQueueItem,
  );
}

export function fetchIdentifyEntity(entityId: string, options?: RequestOptions): Promise<EntityDetailCard> {
  return fetchEntity(entityId, options);
}

export function fetchIdentifyEntities(
  kind: string,
  search?: string,
  options?: RequestOptions,
): Promise<EntityListResponse> {
  return fetchEntities({ kind, query: search }, options);
}

export function closeBulkIdentifySession(_sessionId: string): Promise<void> {
  return Promise.resolve();
}
