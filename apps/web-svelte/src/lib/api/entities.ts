import {
  getEntity,
  getEntityChildren,
  getEntityThumbnails,
  listEntities,
  refreshEntity as refreshEntityRequest,
} from "$lib/api/generated/prismedia";
import type {
  EntityCard as GeneratedEntityCard,
  EntityChildrenBatchGroup as GeneratedEntityChildrenBatchGroup,
  EntityChildrenBatchResponse as GeneratedEntityChildrenBatchResponse,
  EntityGroup as GeneratedEntityGroup,
  EntityListResponse as GeneratedEntityListResponse,
  EntityRefreshResponse,
  EntityThumbnail as GeneratedEntityThumbnail,
  EntityThumbnailBatchResponse as GeneratedEntityThumbnailBatchResponse,
  ListEntitiesParams,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";

export type EntityCard = GeneratedEntityThumbnail;
export type EntityDetailCard = GeneratedEntityCard;
export type EntityCardFull = GeneratedEntityCard;
export type EntityChildGroup = GeneratedEntityGroup;
export type EntityRelationshipGroup = GeneratedEntityGroup;
export type EntityThumbnail = GeneratedEntityThumbnail;
export type EntityListResponse = GeneratedEntityListResponse;

const ENTITY_CHILDREN_BATCH_SIZE = 250;

export interface EntityThumbnailRequestOptions extends RequestOptions {
  hideNsfw?: boolean;
}

export function fetchEntities(
  params?: ListEntitiesParams,
  options?: RequestOptions,
): Promise<EntityListResponse> {
  return listEntities(params, requestInit(options)).then((r) =>
    unwrapGenerated(r, "Failed to list entities"),
  );
}

export async function fetchEntityThumbnails(
  ids: string[],
  options?: EntityThumbnailRequestOptions,
): Promise<EntityThumbnail[]> {
  const uniqueIds = [...new Set(ids.filter(Boolean))];
  if (uniqueIds.length === 0) return [];

  const response = await getEntityThumbnails(
    { ids: uniqueIds },
    { hideNsfw: options?.hideNsfw },
    requestInit(options),
  );
  return (response.data as GeneratedEntityThumbnailBatchResponse).items;
}

/**
 * Resolves direct child thumbnails for several Entity parents without hydrating one full detail
 * document per parent. Duplicate and empty identifiers are removed while caller order is retained.
 */
export async function fetchEntityChildren(
  parentIds: string[],
  options?: EntityThumbnailRequestOptions,
): Promise<GeneratedEntityChildrenBatchGroup[]> {
  const uniqueParentIds = [...new Set(parentIds.filter(Boolean))];
  if (uniqueParentIds.length === 0) return [];

  const groups: GeneratedEntityChildrenBatchGroup[] = [];
  for (let start = 0; start < uniqueParentIds.length; start += ENTITY_CHILDREN_BATCH_SIZE) {
    options?.signal?.throwIfAborted();
    const response = await getEntityChildren(
      { parentIds: uniqueParentIds.slice(start, start + ENTITY_CHILDREN_BATCH_SIZE) },
      { hideNsfw: options?.hideNsfw },
      requestInit(options),
    );
    groups.push(...unwrapGenerated<GeneratedEntityChildrenBatchResponse>(
      response,
      "Failed to fetch Entity children",
    ).groups);
  }

  return groups;
}

export function fetchEntity(id: string, options?: RequestOptions): Promise<EntityCardFull> {
  return getEntity(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch entity ${id}`),
  );
}

export function refreshEntity(
  entityId: string,
  options?: RequestOptions,
): Promise<EntityRefreshResponse> {
  return refreshEntityRequest(entityId, requestInit(options)).then((r) =>
    unwrapGenerated(r, "Failed to refresh entity"),
  );
}
