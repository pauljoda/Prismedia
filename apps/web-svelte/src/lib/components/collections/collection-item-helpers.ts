import { buildHrefWithFrom } from "$lib/back-navigation";
import type { CollectionItem } from "$lib/collections/models";

export function getEntityHref(item: CollectionItem, from?: string): string {
  let base: string;
  switch (item.entityType) {
    case "video":
      base = `/videos/${item.entityId}`;
      break;
    case "gallery":
      base = `/galleries/${item.entityId}`;
      break;
    case "book":
      base = `/books/${item.entityId}`;
      break;
    case "image":
      base = `/images/${item.entityId}`;
      break;
    case "audio-track":
      base = `/audio/tracks/${item.entityId}`;
      break;
    default:
      return "#";
  }
  return from ? buildHrefWithFrom(base, from) : base;
}

export function getEntityTitle(item: CollectionItem): string {
  const entity = item.entity as Record<string, unknown> | undefined;
  if (!entity) return "Unknown";
  return (entity.title as string) ?? "Untitled";
}

export function getEntityThumbnail(item: CollectionItem): string | null {
  const entity = item.entity as Record<string, unknown> | undefined;
  if (!entity) return null;
  switch (item.entityType) {
    case "video":
      return ((entity.cardThumbnailPath as string | null) ??
        (entity.thumbnailPath as string | null)) as string | null;
    case "gallery":
      return (entity.coverImagePath as string | null) ?? null;
    case "book":
      return (entity.coverImagePath as string | null) ?? null;
    case "image":
      return (entity.thumbnailPath as string | null) ?? null;
    case "audio-track":
      return null;
    default:
      return null;
  }
}

export function getEntityMeta(item: CollectionItem): string | null {
  const entity = item.entity as Record<string, unknown> | undefined;
  if (!entity) return null;
  switch (item.entityType) {
    case "video": {
      const duration = entity.durationFormatted as string | null;
      const resolution = entity.resolution as string | null;
      return [duration, resolution].filter(Boolean).join(" · ");
    }
    case "gallery": {
      const count = entity.imageCount as number | null;
      return count ? `${count} images` : null;
    }
    case "book": {
      const chapters = entity.chapterCount as number | null;
      const pages = entity.pageCount as number | null;
      if (chapters) return `${chapters} chapter${chapters === 1 ? "" : "s"}`;
      return pages ? `${pages} pages` : null;
    }
    case "image": {
      const w = entity.width as number | null;
      const h = entity.height as number | null;
      return w && h ? `${w}×${h}` : null;
    }
    case "audio-track": {
      const d = entity.duration as number | null;
      if (!d) return null;
      const m = Math.floor(d / 60);
      const s = Math.floor(d % 60);
      return `${m}:${String(s).padStart(2, "0")}`;
    }
    default:
      return null;
  }
}
