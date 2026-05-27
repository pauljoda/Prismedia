import {
  addCollectionItems as addCollectionItemsRequest,
  createCollection as createCollectionRequest,
  deleteCollection as deleteCollectionRequest,
  listCollectionItems,
  previewCollectionRules as previewCollectionRulesRequest,
  refreshCollection as refreshCollectionRequest,
  removeCollectionItems as removeCollectionItemsRequest,
  reorderCollectionItems as reorderCollectionItemsRequest,
  updateCollection as updateCollectionRequest,
} from "$lib/api/generated/prismedia";
import type {
  CollectionAddItemsRequest as GeneratedCollectionAddItemsRequest,
  CollectionItemsResponse,
  CollectionRemoveItemsRequest,
  CollectionReorderItemsRequest,
  CollectionRulePreviewRequest,
  CollectionRulePreviewResponse as GeneratedCollectionRulePreviewResponse,
  CollectionWriteRequest as GeneratedCollectionWriteRequest,
  CollectionDetail,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";
import type {
  CollectionAddItemsRequest,
  CollectionItem,
  CollectionRulePreviewResponse,
  CollectionWriteRequest,
} from "$lib/collections/models";

interface CollectionItemMutationResponse {
  count: number;
}

interface CollectionRefreshResponse {
  refreshed: boolean;
  itemCount: number;
}

export async function fetchCollectionItems(
  collectionId: string,
  options?: RequestOptions,
): Promise<CollectionItem[]> {
  const response = await listCollectionItems(collectionId, undefined, requestInit(options));
  return unwrapGenerated<CollectionItemsResponse>(
    response,
    "Failed to fetch collection items",
  ).items as CollectionItem[];
}

export function createCollection(
  request: CollectionWriteRequest,
  options?: RequestOptions,
): Promise<CollectionDetail> {
  return createCollectionRequest(
    request as GeneratedCollectionWriteRequest,
    requestInit(options),
  ).then((response) => unwrapGenerated(response, "Failed to create collection", [201]));
}

export function updateCollection(
  collectionId: string,
  request: CollectionWriteRequest,
  options?: RequestOptions,
): Promise<CollectionDetail> {
  return updateCollectionRequest(
    collectionId,
    request as GeneratedCollectionWriteRequest,
    requestInit(options),
  ).then((response) => unwrapGenerated(response, "Failed to update collection"));
}

export function deleteCollection(
  collectionId: string,
  options?: RequestOptions,
): Promise<{ id: string }> {
  return deleteCollectionRequest(collectionId, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to delete collection"),
  );
}

export function addCollectionItems(
  collectionId: string,
  request: CollectionAddItemsRequest,
  options?: RequestOptions,
): Promise<CollectionItemMutationResponse> {
  return addCollectionItemsRequest(
    collectionId,
    request as GeneratedCollectionAddItemsRequest,
    requestInit(options),
  ).then((response) => unwrapGenerated(response, "Failed to add collection items"));
}

export function removeCollectionItems(
  collectionId: string,
  itemIds: string[],
  options?: RequestOptions,
): Promise<CollectionItemMutationResponse> {
  return removeCollectionItemsRequest(
    collectionId,
    { itemIds } as CollectionRemoveItemsRequest,
    requestInit(options),
  ).then((response) => unwrapGenerated(response, "Failed to remove collection items"));
}

export function reorderCollectionItems(
  collectionId: string,
  itemIds: string[],
  options?: RequestOptions,
): Promise<CollectionItemMutationResponse> {
  return reorderCollectionItemsRequest(
    collectionId,
    { itemIds } as CollectionReorderItemsRequest,
    requestInit(options),
  ).then((response) => unwrapGenerated(response, "Failed to reorder collection items"));
}

export function refreshCollection(
  collectionId: string,
  options?: RequestOptions,
): Promise<CollectionRefreshResponse> {
  return refreshCollectionRequest(collectionId, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to refresh collection") as CollectionRefreshResponse,
  );
}

export function previewCollectionRules(
  ruleTreeJson: string,
  options?: RequestOptions,
): Promise<CollectionRulePreviewResponse> {
  return previewCollectionRulesRequest(
    { ruleTreeJson } as CollectionRulePreviewRequest,
    undefined,
    requestInit(options),
  ).then((response) => {
    const preview = unwrapGenerated<GeneratedCollectionRulePreviewResponse>(
      response,
      "Failed to preview collection rules",
    );
    return {
      ...preview,
      total: Number(preview.total ?? 0),
    } as CollectionRulePreviewResponse;
  });
}
