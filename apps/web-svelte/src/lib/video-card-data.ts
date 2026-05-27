import type { EntityThumbnail } from "$lib/api/generated/model";
import {
  getFlagsCapability,
  getRatingValue,
  getTags,
  getTechnicalCapability,
  getThumbnailUrl,
} from "$lib/api/capabilities";
import { apiAssetUrl as toApiUrl } from "$lib/api/orval-fetch";
import { formatResolutionLabel } from "$lib/utils/format";

export interface VideoCardPerformer {
  name: string;
  imagePath?: string;
  isNsfw?: boolean;
}

export interface VideoCardTag {
  name: string;
  isNsfw: boolean;
}

export interface VideoCardData {
  id: string;
  href: string;
  title: string;
  thumbnail?: string;
  cardThumbnail?: string;
  trickplaySprite?: string;
  trickplayVtt?: string;
  scrubDurationSeconds?: number;
  duration?: string;
  resolution?: string;
  codec?: string;
  fileSize?: string;
  studio?: string;
  performers?: VideoCardPerformer[];
  tags?: VideoCardTag[];
  rating?: number;
  views?: number;
  isNsfw?: boolean;
  hasSubtitles?: boolean;
  seasonNumber?: number;
  episodeNumber?: number;
}

function buildHrefWithFrom(href: string, from?: string): string {
  if (!from) return href;
  const sep = href.includes("?") ? "&" : "?";
  return `${href}${sep}from=${encodeURIComponent(from)}`;
}

interface VideoLike {
  id: string;
  title: string;
  thumbnailPath?: string | null;
  cardThumbnailPath?: string | null;
  spritePath?: string | null;
  trickplayVttPath?: string | null;
  duration?: number | null;
  durationFormatted?: string | null;
  resolution?: string | null;
  codec?: string | null;
  fileSizeFormatted?: string | null;
  performers?: Array<{ name: string; imagePath?: string | null; isNsfw?: boolean }>;
  tags?: Array<{ name: string; isNsfw: boolean }>;
  rating?: number | null;
  playCount?: number;
  isNsfw?: boolean;
  hasSubtitles?: boolean;
  seasonNumber?: number | null;
  episodeNumber?: number | null;
  updatedAt?: string;
}

function formatTimeSpan(ts: string): string {
  const parts = ts.split(":");
  if (parts.length < 3) return ts;
  const hours = parseInt(parts[0], 10);
  const minutes = parts[1];
  const seconds = parts[2].split(".")[0];
  return hours > 0 ? `${hours}:${minutes}:${seconds}` : `${minutes}:${seconds}`;
}

function formatResolution(width: number | string | null, height: number | string | null): string | undefined {
  const h = typeof height === "number" ? height : parseInt(String(height), 10);
  if (!h || isNaN(h)) return undefined;
  return formatResolutionLabel(h) ?? undefined;
}

export function entityCardToVideoCardData(item: EntityThumbnail): VideoCardData {
  return {
    id: item.id,
    href: `/videos/${item.id}`,
    title: item.title,
    thumbnail: item.coverUrl ?? undefined,
    duration: item.meta.find((meta) => meta.icon === "duration")?.label,
    resolution: item.meta.find((meta) => meta.icon === "video" || meta.icon === "image")?.label,
    codec: undefined,
    tags: [],
    rating: typeof item.rating === "number" && item.rating > 0 ? item.rating * 20 : undefined,
    isNsfw: item.isNsfw,
  };
}

export function videoListItemToCardData(video: VideoLike, from?: string): VideoCardData {
  const base = `/videos/${video.id}`;
  const performers = video.performers ?? [];
  const tags = video.tags ?? [];
  return {
    id: video.id,
    href: buildHrefWithFrom(base, from),
    title: video.title,
    thumbnail: toApiUrl(video.thumbnailPath, video.updatedAt),
    cardThumbnail: video.thumbnailPath?.includes("thumb-custom")
      ? undefined
      : toApiUrl(video.cardThumbnailPath, video.updatedAt),
    trickplaySprite: toApiUrl(video.spritePath, video.updatedAt),
    trickplayVtt: toApiUrl(video.trickplayVttPath, video.updatedAt),
    scrubDurationSeconds: video.duration ?? undefined,
    duration: video.durationFormatted ?? undefined,
    resolution: video.resolution ?? undefined,
    codec: video.codec ?? undefined,
    fileSize: video.fileSizeFormatted ?? undefined,
    performers: performers.map((p) => ({
      name: p.name,
      imagePath: toApiUrl(p.imagePath) ?? undefined,
      isNsfw: p.isNsfw,
    })),
    tags: tags.map((t) => ({ name: t.name, isNsfw: t.isNsfw })),
    rating: video.rating ?? undefined,
    views: video.playCount,
    isNsfw: video.isNsfw,
    hasSubtitles: video.hasSubtitles ?? false,
    seasonNumber: video.seasonNumber ?? undefined,
    episodeNumber: video.episodeNumber ?? undefined,
  };
}
