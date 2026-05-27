import { fetchApi } from "$lib/api/orval-fetch";
import type { CollectionItem } from "$lib/collections/models";

interface CollectionItemsResponse {
  items: CollectionItem[];
}

export async function fetchCollectionItems(collectionId: string): Promise<CollectionItem[]> {
  const response = await fetchApi<CollectionItemsResponse>(`/collections/${collectionId}/items`);
  return response.items;
}
