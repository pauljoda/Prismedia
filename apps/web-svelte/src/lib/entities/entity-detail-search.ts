import { listEntities } from "$lib/api/generated/prismedia";
import type { EntityListResponse } from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";
import type { EntityPickerItem } from "$lib/components/forms/EntityPicker.svelte";
import { ENTITY_KIND, type EntityKindCode } from "$lib/entities/entity-codes";

function mapItems(items: Array<{ id: string; title: string; coverUrl?: string | null }>): EntityPickerItem[] {
  return items.map((item) => ({
    id: item.id,
    title: item.title,
    thumbnailUrl: item.coverUrl ?? null,
  }));
}

async function searchEntities(kind: EntityKindCode, query: string): Promise<EntityPickerItem[]> {
  const response = await listEntities({ kind, query: query || undefined, limit: 20 });
  return mapItems(
    unwrapGenerated<EntityListResponse>(response, `Failed to search ${kind}`).items,
  );
}

export function searchTags(query: string): Promise<EntityPickerItem[]> {
  return searchEntities(ENTITY_KIND.tag, query);
}

export function searchPeople(query: string): Promise<EntityPickerItem[]> {
  return searchEntities(ENTITY_KIND.person, query);
}

export function searchStudios(query: string): Promise<EntityPickerItem[]> {
  return searchEntities(ENTITY_KIND.studio, query);
}
