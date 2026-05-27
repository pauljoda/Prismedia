import { listTags, listPeople, listStudios } from "$lib/api/generated/prismedia";
import type { EntityPickerItem } from "$lib/components/forms/EntityPicker.svelte";

function mapItems(items: Array<{ id: string; title: string; coverUrl?: string | null }>): EntityPickerItem[] {
  return items.map((item) => ({
    id: item.id,
    title: item.title,
    thumbnailUrl: item.coverUrl ?? null,
  }));
}

export async function searchTags(query: string): Promise<EntityPickerItem[]> {
  const params = query ? { query, limit: 20 } : { limit: 20 };
  const response = await listTags(params);
  return mapItems(response.data.items);
}

export async function searchPeople(query: string): Promise<EntityPickerItem[]> {
  const params = query ? { query, limit: 20 } : { limit: 20 };
  const response = await listPeople(params);
  return mapItems(response.data.items);
}

export async function searchStudios(query: string): Promise<EntityPickerItem[]> {
  const params = query ? { query, limit: 20 } : { limit: 20 };
  const response = await listStudios(params);
  return mapItems(response.data.items);
}
