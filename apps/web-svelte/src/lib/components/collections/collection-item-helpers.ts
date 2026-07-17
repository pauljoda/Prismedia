import { buildHrefWithFrom } from "$lib/back-navigation";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import type { CollectionItem } from "$lib/collections/models";

export function getEntityHref(item: CollectionItem, from?: string): string {
  let base: string;
  switch (item.entityType) {
    case ENTITY_KIND.movie:
      base = `/movies/${item.entityId}`;
      break;
    case ENTITY_KIND.video:
      base = `/videos/${item.entityId}`;
      break;
    case ENTITY_KIND.videoSeries:
      base = `/series/${item.entityId}`;
      break;
    case ENTITY_KIND.gallery:
      base = `/galleries/${item.entityId}`;
      break;
    case ENTITY_KIND.book:
      base = `/books/${item.entityId}`;
      break;
    case ENTITY_KIND.image:
      base = `/images/${item.entityId}`;
      break;
    case ENTITY_KIND.audioTrack:
      base = `/audio/tracks/${item.entityId}`;
      break;
    default:
      return "#";
  }
  return from ? buildHrefWithFrom(base, from) : base;
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
