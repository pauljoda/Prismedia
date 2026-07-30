import { buildHrefWithFrom } from "$lib/back-navigation";
import { resolveEntityHref } from "$lib/entities/entity-codes";
import type { CollectionItem } from "$lib/collections/models";

export function getEntityHref(item: CollectionItem, from?: string): string {
  const base = resolveEntityHref(item.entityType, item.entityId);
  return base ? buildHrefWithFrom(base, from ?? "") : "#";
}

export function getEntityTitle(item: CollectionItem): string {
  const entity = item.entity;
  if (!entity) return "Unknown";
  return entity.title ?? "Untitled";
}

export function getEntityThumbnail(item: CollectionItem): string | null {
  const entity = item.entity;
  if (!entity) return null;
  return entity.coverUrl;
}

export function getEntityMeta(item: CollectionItem): string | null {
  const entity = item.entity;
  if (!entity) return null;
  const labels = entity.meta.map((meta) => meta.label).filter(Boolean);
  return labels.length > 0 ? labels.join(" · ") : null;
}
