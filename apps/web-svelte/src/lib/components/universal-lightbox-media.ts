import type { EntityCapability } from "$lib/api/generated/model";
import { entityFileUrl } from "$lib/api/prismedia";
import { getCapability, isNsfw } from "$lib/api/capabilities";
import { CAPABILITY_KIND, ENTITY_FILE_ROLE, ENTITY_KIND, type EntityFileRoleCode } from "$lib/entities/entity-codes";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

export interface UniversalLightboxEntity {
  id: string;
  kind: string;
  title: string;
  capabilities: EntityCapability[];
  coverUrl?: string | null;
  isNsfw?: boolean;
  rating?: number | string | null;
}

export interface UniversalLightboxSource {
  src: string;
  role: string;
}

export interface UniversalLightboxVideoSource {
  src: string;
  type?: string | null;
  quality: "original" | "fallback";
}

const videoExtensions = new Set([
  "mp4",
  "m4v",
  "mov",
  "webm",
  "mkv",
  "avi",
  "wmv",
  "flv",
  "ogg",
  "ogv",
]);

const videoContainers = new Set([
  "mp4",
  "mpeg4",
  "quicktime",
  "mov",
  "webm",
  "matroska",
  "mkv",
  "avi",
  "wmv",
  "flv",
  "ogg",
]);

const videoMimeTypes = new Map<string, string>([
  ["mp4", "video/mp4"],
  ["m4v", "video/mp4"],
  ["h264", "video/mp4"],
  ["mpeg4", "video/mp4"],
  ["mov", "video/quicktime"],
  ["quicktime", "video/quicktime"],
  ["hevc", "video/quicktime"],
  ["h265", "video/quicktime"],
  ["webm", "video/webm"],
  ["vp8", "video/webm"],
  ["vp9", "video/webm"],
  ["av1", "video/webm"],
  ["mkv", "video/x-matroska"],
  ["matroska", "video/x-matroska"],
  ["avi", "video/x-msvideo"],
  ["wmv", "video/x-ms-wmv"],
  ["flv", "video/x-flv"],
  ["ogg", "video/ogg"],
  ["ogv", "video/ogg"],
]);

export function lightboxEntityFromCard(card: EntityThumbnailCard): UniversalLightboxEntity {
  return {
    id: card.entity.id,
    kind: card.entity.kind,
    title: card.entity.title,
    capabilities: card.entity.capabilities,
    coverUrl: card.cover?.src ?? null,
    isNsfw: isNsfw(card.entity.capabilities),
  };
}

export function buildLightboxImageSource(entity: UniversalLightboxEntity): UniversalLightboxSource | null {
  if (hasFileRole(entity, ENTITY_FILE_ROLE.source)) {
    return { src: entityFileUrl(entity.id, ENTITY_FILE_ROLE.source), role: ENTITY_FILE_ROLE.source };
  }

  const images = getCapability(entity.capabilities, CAPABILITY_KIND.images);
  const asset =
    images?.items.find((item) => item.kind === ENTITY_FILE_ROLE.full) ??
    images?.items.find((item) => item.kind === ENTITY_FILE_ROLE.cover) ??
    images?.items.find((item) => item.kind === ENTITY_FILE_ROLE.poster) ??
    images?.items.find((item) => item.kind === ENTITY_FILE_ROLE.thumbnail);

  if (asset?.path) {
    return { src: asset.path, role: String(asset.kind) };
  }

  const fallback = images?.coverUrl ?? images?.thumbnailUrl ?? entity.coverUrl ?? null;
  return fallback ? { src: fallback, role: "cover" } : null;
}

export function buildLightboxVideoSources(entity: UniversalLightboxEntity): UniversalLightboxVideoSource[] {
  const files = getCapability(entity.capabilities, CAPABILITY_KIND.files)?.items ?? [];
  const sources: UniversalLightboxVideoSource[] = [];
  const seen = new Set<string>();

  function add(role: EntityFileRoleCode, quality: UniversalLightboxVideoSource["quality"], type?: string | null) {
    if (!files.some((file) => file.role === role)) return;
    const src = entityFileUrl(entity.id, role);
    if (seen.has(src)) return;
    seen.add(src);
    sources.push({ src, type, quality });
  }

  const sourceFile = files.find((file) => file.role === ENTITY_FILE_ROLE.source);
  const previewFile = files.find((file) => file.role === ENTITY_FILE_ROLE.preview);

  add(ENTITY_FILE_ROLE.source, "original", sourceFile?.mimeType ?? mimeTypeForEntity(entity));
  add(ENTITY_FILE_ROLE.preview, "fallback", previewFile?.mimeType ?? "video/mp4");

  return sources;
}

export function isLightboxVideoCapable(entity: UniversalLightboxEntity): boolean {
  if (entity.kind === ENTITY_KIND.video) return true;

  const files = getCapability(entity.capabilities, CAPABILITY_KIND.files)?.items ?? [];
  if (files.some((file) => file.mimeType?.toLowerCase().startsWith("video/"))) return true;
  if (files.some((file) => videoExtensions.has(extensionOf(file.path)))) return true;

  const technical = getCapability(entity.capabilities, CAPABILITY_KIND.technical);
  if (positiveNumber(technical?.duration)) return true;
  if (technical?.container && videoContainers.has(technical.container.toLowerCase())) return true;
  if (technical?.format && videoContainers.has(technical.format.toLowerCase())) return true;

  return videoExtensions.has(extensionOf(entity.title));
}

export function mimeTypeForEntity(entity: UniversalLightboxEntity): string | undefined {
  const technical = getCapability(entity.capabilities, CAPABILITY_KIND.technical);
  const normalizedFormat = technical?.format?.toLowerCase();
  if (normalizedFormat && videoMimeTypes.has(normalizedFormat)) {
    return videoMimeTypes.get(normalizedFormat);
  }
  const normalizedContainer = technical?.container?.toLowerCase();
  if (normalizedContainer && videoMimeTypes.has(normalizedContainer)) {
    return videoMimeTypes.get(normalizedContainer);
  }
  return videoMimeTypes.get(extensionOf(entity.title));
}

function hasFileRole(entity: UniversalLightboxEntity, role: EntityFileRoleCode): boolean {
  return getCapability(entity.capabilities, CAPABILITY_KIND.files)?.items.some((file) => file.role === role) === true;
}

function extensionOf(path: string | null | undefined): string {
  const extension = path?.split("?")[0]?.split("#")[0]?.split(".").pop()?.toLowerCase();
  return extension && extension !== path ? extension : "";
}

function positiveNumber(value: number | string | null | undefined): boolean {
  if (typeof value === "number") return Number.isFinite(value) && value > 0;
  if (typeof value !== "string" || value.trim() === "") return false;
  if (/^\d{1,2}:\d{2}:\d{2}/.test(value)) {
    return value !== "00:00:00" && value !== "00:00:00.0000000";
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0;
}
