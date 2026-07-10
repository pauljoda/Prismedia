import {
  getCapability,
  getDescription,
  getImagesCapability,
  getRatingValue,
  getTechnicalCapability,
  isNsfw,
  type EntityCapabilityKind,
} from "$lib/api/capabilities";
import { numberValue, formatDurationString, durationToSeconds, formatResolutionLabel } from "$lib/utils/format";
import type {
  EntityCapability,
  EntityCard,
  EntityDate,
  EntityExternalId,
  EntityFingerprint,
  EntityMarker,
  EntitySource,
  EntitySubtitle,
  EntityUrl,
} from "$lib/api/generated/model";
import { CAPABILITY_KIND, ENTITY_FILE_ROLE } from "./entity-codes";
import { creditRoleCharacterSubtitle } from "./entity-credits";
import { entityCardToThumbnailCard, getEntityKindLabel } from "./entity-grid";
import {
  entityReferenceToThumbnailCard,
  type EntityThumbnailCard,
} from "./entity-thumbnail";

/**
 * Lower metadata sections rendered (and edited) by detail routes without tabs whose entity
 * kind owns relationships (galleries, images, audio tracks). Includes the studio/credits
 * editors alongside the scalar metadata editors.
 */
export const DEFAULT_STANDALONE_METADATA_SECTION_IDS = [
  "studio",
  "credits",
  "stats",
  "dates",
  "technical",
  "progress",
  "positions",
  "classification",
  "sources",
  "fingerprints",
  "links",
];

/**
 * Lower metadata sections for reference/taxonomy entities (people, studios, tags) that are
 * pointed AT by relationships rather than owning them. The backend ignores studio/credits
 * patches for these kinds, so the editors are omitted instead of silently no-oping.
 */
export const REFERENCE_STANDALONE_METADATA_SECTION_IDS =
  DEFAULT_STANDALONE_METADATA_SECTION_IDS.filter((id) => id !== "studio" && id !== "credits");

/** Entity payload consumed by the shared detail surface. */
export interface EntityDetailEntity extends Omit<EntityCard, "sortOrder" | "relationships"> {
  sortOrder?: EntityCard["sortOrder"];
  relationships?: EntityCard["relationships"];
  capabilities: EntityCapability[];
}

/** Resolved hero/banner image displayed as a full-width backdrop at the top of the detail view. */
export interface EntityDetailHero {
  src: string;
  alt: string;
}

/** Resolved poster/cover image displayed alongside the entity title and metadata. */
export interface EntityDetailPoster {
  src: string;
  alt: string;
}

/** Interactive star rating state. */
export interface EntityDetailRating {
  value: number;
  max: number;
}

/** Boolean flag displayed as a status badge. */
export interface EntityDetailFlag {
  code: "favorite" | "nsfw" | "organized";
  label: string;
  active: boolean;
}

/** A stat or counter item shown in the metadata grid. */
export interface EntityDetailStat {
  code: string;
  label: string;
  value: string;
}

/** A formatted date entry. */
export interface EntityDetailDate {
  code: string;
  label: string;
  /** Raw capability value; what edit drafts and metadata patches round-trip. */
  value: string;
  /** Humanized value for display surfaces (hero meta, metadata cards). */
  display: string;
  sortable: string | null;
}

/** Technical specification row. */
export interface EntityDetailTechnicalRow {
  label: string;
  value: string;
}

/** A linked person/performer in the credits section. */
export interface EntityDetailCredit {
  id: string;
  kind: string;
  title: string;
  thumbnail: string | null;
  /**
   * Distinct credit role codes linked to the person, primary first. Editors round-trip
   * the full list so secondary roles (e.g. director and writer) survive metadata saves.
   */
  roles: string[];
  /** Distinct characters linked to the person, primary first. Round-tripped like roles. */
  characters: string[];
}

/** A linked tag shown in the shared detail tag row. */
export interface EntityDetailTag {
  id: string;
  kind: string;
  title: string;
  href: string | null;
}

/** An external link or ID. */
export interface EntityDetailLink {
  label: string;
  url: string | null;
  provider?: string;
}

/** The explicit plugin and persistent identity selected to drive metadata and monitoring. */
export interface EntityDetailProviderIdentity {
  pluginId: string;
  identityNamespace: string;
  identityValue: string;
  url: string | null;
}

/** A file entry. */
export interface EntityDetailFile {
  role: string;
  path: string;
  mimeType: string | null;
}

/** A video/audio marker. */
export interface EntityDetailMarker {
  id: string;
  title: string;
  timestamp: string;
  seconds: number;
  endSeconds: number | null;
}

/** A subtitle track entry. */
export interface EntityDetailSubtitle {
  id: string;
  language: string;
  label: string | null;
  format: string;
  source: string;
  isDefault: boolean;
}

/** Progress state for playback/reading. */
export interface EntityDetailProgress {
  index: number;
  total: number;
  percent: number;
  unit: string;
  mode: string | null;
  completed: boolean;
}

/** A position badge (season, episode, chapter, etc.). */
export interface EntityDetailPosition {
  code: string;
  value: number;
  label: string;
}

/** Classification badge. */
export interface EntityDetailClassification {
  value: string;
  label: string;
  system: string | null;
}

/** Universal view model consumed by the shared EntityDetail component. */
export interface EntityDetailCard {
  entity: EntityDetailEntity;
  kindLabel: string;
  hero: EntityDetailHero | null;
  poster: EntityDetailPoster | null;
  posterCard: EntityThumbnailCard | null;
  description: string | null;
  rating: EntityDetailRating | null;
  flags: EntityDetailFlag[];
  tags: EntityDetailTag[];
  links: EntityDetailLink[];
  providerIdentity: EntityDetailProviderIdentity | null;
  files: EntityDetailFile[];
  presentCapabilities: EntityCapabilityKind[];
}

/** Extended view model with kind-specific fields for entity detail pages. */
export interface EntityDetailCardFull extends EntityDetailCard {
  studio: EntityDetailCredit | null;
  credits: EntityDetailCredit[];
  stats: EntityDetailStat[];
  dates: EntityDetailDate[];
  technical: EntityDetailTechnicalRow[];
  fingerprints: EntityFingerprint[];
  markers: EntityDetailMarker[];
  subtitles: EntityDetailSubtitle[];
  progress: EntityDetailProgress | null;
  positions: EntityDetailPosition[];
  classification: EntityDetailClassification | null;
  sources: EntitySource[];
}


function formatResolution(width: number, height: number): string {
  const label = formatResolutionLabel(height);
  return label ? `${width}×${height} (${label})` : `${width}×${height}`;
}

/**
 * Friendly labels for the date codes providers commonly emit. Codes outside the
 * map fall back to a generic title-cased form of the code itself.
 */
const DATE_CODE_LABELS: Record<string, string> = {
  added: "Added",
  air: "Aired",
  birth: "Born",
  created: "Created",
  death: "Died",
  ended: "Ended",
  firstair: "First aired",
  lastair: "Last aired",
  published: "Published",
  release: "Released",
  released: "Released",
  scanned: "Scanned",
  started: "Started",
  updated: "Updated",
  uploaded: "Uploaded",
};

function formatDateCode(code: string): string {
  const known = DATE_CODE_LABELS[code.trim().toLowerCase().replaceAll("-", "")];
  if (known) return known;
  return code
    .replaceAll("-", " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .toLowerCase()
    .replace(/^\w/, (c) => c.toUpperCase());
}

const MONTH_DATE = /^(\d{4})-(\d{2})$/;
const DAY_DATE = /^(\d{4})-(\d{2})-(\d{2})/;

/**
 * Humanizes an ISO-ish date string for display ("2026-03-20" → "Mar 20, 2026",
 * "2026-03" → "Mar 2026"). Values that are not recognizable dates pass through
 * unchanged; the raw value stays available for editing and sorting.
 */
export function formatDetailDateValue(value: string): string {
  const trimmed = value.trim();
  const day = DAY_DATE.exec(trimmed);
  if (day) {
    const parsed = new Date(Date.UTC(Number(day[1]), Number(day[2]) - 1, Number(day[3])));
    if (!Number.isNaN(parsed.getTime())) {
      return parsed.toLocaleDateString(undefined, {
        year: "numeric",
        month: "short",
        day: "numeric",
        timeZone: "UTC",
      });
    }
  }
  const month = MONTH_DATE.exec(trimmed);
  if (month) {
    const parsed = new Date(Date.UTC(Number(month[1]), Number(month[2]) - 1, 1));
    if (!Number.isNaN(parsed.getTime())) {
      return parsed.toLocaleDateString(undefined, { year: "numeric", month: "short", timeZone: "UTC" });
    }
  }
  return trimmed;
}

function formatStatCode(code: string): string {
  return code.replaceAll("-", " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

const HIDDEN_STAT_CODES = new Set(["popularity"]);

function shouldDisplayStat(code: string): boolean {
  return !HIDDEN_STAT_CODES.has(code.trim().toLowerCase());
}

function formatClassificationLabel(kind: string, system: string | null | undefined): string {
  if (!system || system === "manual") return "Classification";
  if (system === "plugin") {
    if (kind === "person") return "Known For";
    return "Category";
  }

  return formatStatCode(system);
}

function markerTimestamp(seconds: number): string {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = Math.floor(seconds % 60);
  if (h > 0) return `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
  return `${m}:${String(s).padStart(2, "0")}`;
}

function resolveHero(capabilities: EntityCapability[]): EntityDetailHero | null {
  const images = getImagesCapability(capabilities);
  if (!images) return null;
  const coverItem = images.items.find(
    (item) => item.kind === ENTITY_FILE_ROLE.backdrop,
  );
  if (coverItem) return { src: coverItem.path, alt: String(coverItem.kind) };
  return null;
}

function resolvePoster(capabilities: EntityCapability[]): EntityDetailPoster | null {
  const images = getImagesCapability(capabilities);
  if (!images) return null;
  // items arrive priority-sorted by the server's cover selection; accepting every
  // poster-capable role (including cover) keeps the detail poster on the same
  // winner grid cards show instead of skipping past a winning cover-role file.
  const posterItem = images.items.find(
    (item) =>
      item.kind === ENTITY_FILE_ROLE.poster ||
      item.kind === ENTITY_FILE_ROLE.thumbnail ||
      item.kind === ENTITY_FILE_ROLE.cover ||
      item.kind === ENTITY_FILE_ROLE.logo,
  );
  if (posterItem) return { src: posterItem.path, alt: String(posterItem.kind) };
  if (images.coverUrl) return { src: images.coverUrl, alt: "Cover" };
  if (images.thumbnailUrl) return { src: images.thumbnailUrl, alt: "Thumbnail" };
  return null;
}

function resolvePosterCard(entity: EntityCard): EntityThumbnailCard {
  return entityCardToThumbnailCard(entity);
}

function resolveFlags(capabilities: EntityCapability[]): EntityDetailFlag[] {
  const flagsCap = getCapability(capabilities, CAPABILITY_KIND.flags);
  if (!flagsCap) return [];
  const result: EntityDetailFlag[] = [];
  if (flagsCap.isFavorite != null) {
    result.push({ code: "favorite", label: "Favorite", active: flagsCap.isFavorite });
  }
  if (flagsCap.isNsfw != null) {
    result.push({ code: "nsfw", label: "NSFW", active: flagsCap.isNsfw });
  }
  if (flagsCap.isOrganized != null) {
    result.push({ code: "organized", label: "Organized", active: flagsCap.isOrganized });
  }
  return result;
}

function resolveTechnical(capabilities: EntityCapability[]): EntityDetailTechnicalRow[] {
  const tech = getTechnicalCapability(capabilities);
  if (!tech) return [];
  const rows: EntityDetailTechnicalRow[] = [];
  const duration = formatDurationString(tech.duration);
  if (duration) rows.push({ label: "Duration", value: duration });
  const width = numberValue(tech.width);
  const height = numberValue(tech.height);
  if (width && height) rows.push({ label: "Resolution", value: formatResolution(width, height) });
  const frameRate = numberValue(tech.frameRate);
  if (frameRate) rows.push({ label: "Frame Rate", value: `${frameRate} fps` });
  const sampleRate = numberValue(tech.sampleRate);
  if (sampleRate) rows.push({ label: "Sample Rate", value: `${(sampleRate / 1000).toFixed(1)} kHz` });
  const channels = numberValue(tech.channels);
  if (channels) rows.push({ label: "Channels", value: String(channels) });
  if (tech.codec) rows.push({ label: "Codec", value: tech.codec });
  if (tech.container) rows.push({ label: "Container", value: tech.container });
  if (tech.format) rows.push({ label: "Format", value: tech.format });
  return rows;
}

function resolveLinks(capabilities: EntityCapability[]): EntityDetailLink[] {
  const linksCap = getCapability(capabilities, CAPABILITY_KIND.links);
  if (!linksCap) return [];
  const result: EntityDetailLink[] = [];
  for (const url of linksCap.urls) {
    result.push({ label: url.label ?? url.value, url: url.value });
  }
  for (const ext of linksCap.externalIds) {
    result.push({ label: `${ext.provider}: ${ext.value}`, url: ext.url, provider: ext.provider });
  }
  return result;
}

function resolveProviderIdentity(capabilities: EntityCapability[]): EntityDetailProviderIdentity | null {
  const identity = getCapability(capabilities, CAPABILITY_KIND.providerIdentity);
  if (!identity) return null;
  return {
    pluginId: identity.pluginId,
    identityNamespace: identity.identityNamespace,
    identityValue: identity.identityValue,
    url: identity.url,
  };
}

function resolveMarkers(capabilities: EntityCapability[]): EntityDetailMarker[] {
  const markersCap = getCapability(capabilities, CAPABILITY_KIND.markers);
  if (!markersCap) return [];
  return markersCap.items.map((item) => {
    const sec = numberValue(item.seconds) ?? 0;
    const endSec = numberValue(item.endSeconds);
    return {
      id: item.id,
      title: item.title,
      timestamp: markerTimestamp(sec),
      seconds: sec,
      endSeconds: endSec,
    };
  });
}

function resolveSubtitles(capabilities: EntityCapability[]): EntityDetailSubtitle[] {
  const subsCap = getCapability(capabilities, CAPABILITY_KIND.subtitles);
  if (!subsCap) return [];
  return subsCap.items.map((item) => ({
    id: item.id,
    language: item.language,
    label: item.label,
    format: item.format,
    source: String(item.source),
    isDefault: item.isDefault,
  }));
}

function resolveProgress(capabilities: EntityCapability[]): EntityDetailProgress | null {
  const progressCap = getCapability(capabilities, CAPABILITY_KIND.progress);
  if (!progressCap) return null;
  const rawIndex = Math.max(0, numberValue(progressCap.index) ?? 0);
  const total = Math.max(0, numberValue(progressCap.total) ?? 0);
  const completed = Boolean(progressCap.completedAt);
  const index = total > 0
    ? (completed ? total : Math.min(rawIndex + 1, total))
    : 0;
  const percent = total > 0 ? Math.round((index / total) * 100) : 0;
  return {
    index,
    total,
    percent,
    unit: progressCap.unit,
    mode: progressCap.mode,
    completed,
  };
}

function resolvePositions(capabilities: EntityCapability[]): EntityDetailPosition[] {
  const positionCap = getCapability(capabilities, CAPABILITY_KIND.position);
  if (!positionCap) return [];
  return positionCap.items.map((item) => ({
    code: item.code,
    value: numberValue(item.value) ?? 0,
    label: item.label ?? `${formatStatCode(item.code)} ${item.value}`,
  }));
}

/**
 * Converts a generic entity card into the complete detail view model.
 * Reads only shared capabilities so every entity kind can flow through
 * one detail component — the same philosophy as entityCardToThumbnailCard.
 */
export function entityCardToDetailCard(entity: EntityCard): EntityDetailCardFull {
  const capabilities = entity.capabilities;
  const presentCapabilities = [
    ...new Set(capabilities.map((c) => c.kind)),
  ] as EntityCapabilityKind[];
  const poster = resolvePoster(capabilities);

  const statsCap = getCapability(capabilities, CAPABILITY_KIND.stats);
  const datesCap = getCapability(capabilities, CAPABILITY_KIND.dates);
  const filesCap = getCapability(capabilities, CAPABILITY_KIND.files);
  const fingerprintsCap = getCapability(capabilities, CAPABILITY_KIND.fingerprints);
  const sourcesCap = getCapability(capabilities, CAPABILITY_KIND.source);
  const classificationCap = getCapability(capabilities, CAPABILITY_KIND.classification);
  const ratingValue = getRatingValue(capabilities);

  return {
    entity: { ...entity, capabilities },
    kindLabel: getEntityKindLabel(entity.kind),
    hero: resolveHero(capabilities),
    poster,
    posterCard: resolvePosterCard(entity),
    description: getDescription(capabilities),
    rating: getCapability(capabilities, CAPABILITY_KIND.rating)
      ? { value: ratingValue, max: 5 }
      : null,
    flags: resolveFlags(capabilities),
    tags: [],
    studio: null,
    credits: [],
    stats: (statsCap?.items ?? [])
      .filter((item) => shouldDisplayStat(item.code))
      .map((item) => ({
        code: item.code,
        label: formatStatCode(item.code),
        value: String(item.value),
      })),
    dates: (datesCap?.items ?? []).map((item) => ({
      code: item.code,
      label: formatDateCode(item.code),
      value: item.value,
      display: formatDetailDateValue(item.value),
      sortable: item.sortableValue ?? null,
    })),
    technical: resolveTechnical(capabilities),
    links: resolveLinks(capabilities),
    providerIdentity: resolveProviderIdentity(capabilities),
    files: (filesCap?.items ?? []).map((item) => ({
      role: String(item.role),
      path: item.path,
      mimeType: item.mimeType,
    })),
    fingerprints: fingerprintsCap?.items ?? [],
    markers: resolveMarkers(capabilities),
    subtitles: resolveSubtitles(capabilities),
    progress: resolveProgress(capabilities),
    positions: resolvePositions(capabilities),
    classification: classificationCap?.value
      ? {
          value: classificationCap.value,
          label: formatClassificationLabel(entity.kind, classificationCap.system),
          system: classificationCap.system,
        }
      : null,
    sources: sourcesCap?.items ?? [],
    presentCapabilities,
  };
}

/**
 * Maps a detail-card credit (person or studio reference) to the shared thumbnail card used
 * by the credits/studio display rails, with the character/role subtitle when known.
 */
export function creditToThumbnailCard(credit: EntityDetailCredit): EntityThumbnailCard {
  return entityReferenceToThumbnailCard(
    {
      id: credit.id,
      kind: credit.kind as EntityDetailEntity["kind"],
      title: credit.title,
      thumbnailUrl: credit.thumbnail,
    },
    { subtitle: creditRoleCharacterSubtitle(credit.roles[0] ?? null, credit.characters[0] ?? null) },
  );
}

/** Returns whether this detail card has any visual hero/banner imagery. */
export function hasHero(card: EntityDetailCard): boolean {
  return card.hero !== null;
}

/** Returns whether this detail card has a sidebar poster/cover image. */
export function hasPoster(card: EntityDetailCard): boolean {
  return card.poster !== null;
}

/** Returns all capability section names that have renderable content. */
export function presentSections(card: EntityDetailCard | EntityDetailCardFull): string[] {
  const sections: string[] = [];
  if (card.description) sections.push("description");
  if (card.rating) sections.push("rating");
  if (card.flags.length > 0) sections.push("flags");
  if (card.tags.length > 0) sections.push("tags");
  if (card.links.length > 0) sections.push("links");
  const full = card as EntityDetailCardFull;
  if (full.studio) sections.push("studio");
  if (full.credits?.length > 0) sections.push("credits");
  if ((full.stats?.length ?? 0) > 0) sections.push("stats");
  if (full.dates?.length > 0) sections.push("dates");
  if (full.technical?.length > 0) sections.push("technical");
  if (full.fingerprints?.length > 0) sections.push("fingerprints");
  if (full.markers?.length > 0) sections.push("markers");
  if (full.subtitles?.length > 0) sections.push("subtitles");
  if (full.progress) sections.push("progress");
  if (full.positions?.length > 0) sections.push("positions");
  if (full.classification) sections.push("classification");
  if (full.sources?.length > 0) sections.push("sources");
  return sections;
}
