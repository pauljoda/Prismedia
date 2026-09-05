import { listEntities } from "$lib/api/generated/prismedia";
import type { EntityListResponse } from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";
import type { EntityPickerItem } from "$lib/components/forms/EntityPicker.svelte";
import { ENTITY_KIND, type EntityKindCode } from "$lib/entities/entity-codes";

function mapItems(items: Array<{ id: string; title: string; coverUrl?: string | null; coverThumbUrl?: string | null; subtitle?: string | null }>): EntityPickerItem[] {
  return items.map((item) => ({
    id: item.id,
    title: item.title,
    thumbnailUrl: item.coverThumbUrl ?? item.coverUrl ?? null,
    subtitle: item.subtitle ?? undefined,
  }));
}

/** Shared bounded Entity search for detail, credit, and rule pickers. Keeps API ordering and thumbnail artwork. */
export async function searchEntityPickerItems(
  kind: EntityKindCode, query: string, options: { hideNsfw?: boolean } = {},
): Promise<EntityPickerItem[]> {
  const response = await listEntities({ kind, query: query || undefined, limit: 20, ...options });
  return mapItems(
    unwrapGenerated<EntityListResponse>(response, `Failed to search ${kind}`).items,
  );
}

export function searchTags(query: string): Promise<EntityPickerItem[]> {
  return searchEntityPickerItems(ENTITY_KIND.tag, query);
}

export function searchPeople(query: string): Promise<EntityPickerItem[]> {
  return searchEntityPickerItems(ENTITY_KIND.person, query);
}

export function searchStudios(query: string): Promise<EntityPickerItem[]> {
  return searchEntityPickerItems(ENTITY_KIND.studio, query);
}
