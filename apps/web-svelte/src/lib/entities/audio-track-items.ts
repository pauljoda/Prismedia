import type { AudioPlaybackItem, EntityCard, EntityThumbnail } from "$lib/api/generated/model";
import { getCapability, getEmbeddedAudioMetadataCapability } from "$lib/api/capabilities";
import { THUMBNAIL_META_ICON } from "$lib/api/generated/codes";
import { CAPABILITY_KIND, ENTITY_FILE_ROLE } from "$lib/entities/entity-codes";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";

export interface EntityThumbnailTrackItemOptions {
  sectionLabel?: string | null;
  sectionKey?: string | null;
  libraryId?: string | null;
  embeddedArtist?: string | null;
  embeddedAlbum?: string | null;
}

function parseDurationString(value: string | null | undefined): number | null {
  if (!value) return null;
  const match = /^-?(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?$/.exec(value);
  if (!match) return null;
  const days = match[1] ? Number(match[1]) : 0;
  const hours = Number(match[2]);
  const minutes = Number(match[3]);
  const seconds = Number(match[4]);
  const frac = match[5] ? Number(`0.${match[5]}`) : 0;
  return days * 86400 + hours * 3600 + minutes * 60 + seconds + frac;
}

/** Parse a display duration like "12:40" or "1:02:33" into seconds. */
function parseDisplayDuration(label: string): number | null {
  const parts = label.split(":").map(Number);
  if (parts.some((p) => !Number.isFinite(p))) return null;
  if (parts.length === 2) return parts[0]! * 60 + parts[1]!;
  if (parts.length === 3) return parts[0]! * 3600 + parts[1]! * 60 + parts[2]!;
  return null;
}

function toNumber(value: number | string | null | undefined): number | null {
  if (value == null) return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

/** Builds the shared player view model from its exact compact server projection. */
export function audioPlaybackItemToTrackItem(item: AudioPlaybackItem): AudioTrackListItemDto {
  return {
    id: item.id,
    title: item.title,
    date: null,
    rating: toNumber(item.rating),
    organized: item.isOrganized,
    isNsfw: item.isNsfw,
    isWanted: item.isWanted,
    hasSourceMedia: item.hasSourceMedia,
    wantedStatus: null,
    latestAcquisitionStatus: null,
    duration: toNumber(item.durationSeconds),
    bitRate: toNumber(item.bitRate),
    sampleRate: toNumber(item.sampleRate),
    channels: toNumber(item.channels),
    codec: item.codec,
    fileSize: null,
    embeddedArtist: item.embeddedArtist,
    embeddedAlbum: item.embeddedAlbum,
    trackNumber: toNumber(item.sortOrder),
    sectionLabel: item.sectionLabel,
    sectionKey: null,
    waveformPath: item.waveformPath,
    libraryId: item.parentEntityId,
    sortOrder: toNumber(item.sortOrder) ?? 0,
    studioId: null,
    performers: [],
    tags: [],
    accessCount: toNumber(item.accessCount) ?? 0,
    lastActiveAt: item.lastActiveAt,
    createdAt: item.createdAt,
  };
}

/**
 * Build a lightweight track list item from an entity thumbnail summary.
 * Avoids the N+1 pattern of hydrating one full Entity document per track.
 */
export function entityThumbnailToTrackItem(
  thumb: EntityThumbnail,
  libraryId: string | null,
  options: EntityThumbnailTrackItemOptions = {},
): AudioTrackListItemDto {
  const durationMeta = thumb.meta.find((m) => m.icon === THUMBNAIL_META_ICON.duration);
  const codecMeta = thumb.meta.find((m) => m.icon === THUMBNAIL_META_ICON.audio);
  const sectionMeta = thumb.meta.find((m) => m.icon === THUMBNAIL_META_ICON.disc);
  const artistMeta = thumb.meta.find((m) => m.icon === THUMBNAIL_META_ICON.person);

  return {
    id: thumb.id,
    title: thumb.title,
    date: null,
    rating: toNumber(thumb.rating) ?? null,
    organized: thumb.isOrganized,
    isNsfw: thumb.isNsfw,
    isWanted: thumb.isWanted === true,
    hasSourceMedia: thumb.hasSourceMedia === true,
    wantedStatus: thumb.wantedStatus ?? null,
    latestAcquisitionStatus: thumb.latestAcquisitionStatus ?? null,
    duration: durationMeta ? parseDisplayDuration(durationMeta.label) : null,
    bitRate: null,
    sampleRate: null,
    channels: null,
    codec: codecMeta?.label ?? null,
    fileSize: null,
    embeddedArtist: artistMeta?.label ?? options.embeddedArtist ?? null,
    embeddedAlbum: options.embeddedAlbum ?? null,
    trackNumber: toNumber(thumb.sortOrder) ?? null,
    sectionLabel: options.sectionLabel ?? sectionMeta?.label ?? null,
    sectionKey: options.sectionKey ?? null,
    waveformPath: null,
    libraryId: options.libraryId ?? libraryId,
    sortOrder: toNumber(thumb.sortOrder) ?? 0,
    studioId: null,
    performers: [],
    tags: [],
    accessCount: toNumber(thumb.accessCount) ?? 0,
    lastActiveAt: null,
    createdAt: "",
  };
}

/** Builds a playable track item from the canonical Entity document and its audio capability. */
export function entityCardToAudioTrackListItem(detail: EntityCard): AudioTrackListItemDto {
  const technical = getCapability(detail.capabilities, CAPABILITY_KIND.technical);
  const consumption = getCapability(detail.capabilities, CAPABILITY_KIND.consumption);
  const rating = getCapability(detail.capabilities, CAPABILITY_KIND.rating);
  const flags = getCapability(detail.capabilities, CAPABILITY_KIND.flags);
  const files = getCapability(detail.capabilities, CAPABILITY_KIND.files);
  const embeddedAudio = getEmbeddedAudioMetadataCapability(detail.capabilities);

  const waveformFile = files?.items.find(
    (file) => file.role === ENTITY_FILE_ROLE.waveform,
  );

  return {
    id: detail.id,
    title: detail.title,
    date: null,
    rating: toNumber(rating?.value) ?? null,
    organized: flags?.isOrganized === true,
    isNsfw: flags?.isNsfw === true,
    isWanted: flags?.isWanted === true,
    hasSourceMedia: files?.items.some((file) => file.role === ENTITY_FILE_ROLE.source) === true,
    wantedStatus: null,
    latestAcquisitionStatus: null,
    duration: parseDurationString(technical?.duration) ?? null,
    bitRate: toNumber(technical?.bitRate) ?? null,
    sampleRate: toNumber(technical?.sampleRate) ?? null,
    channels: toNumber(technical?.channels) ?? null,
    codec: technical?.codec ?? null,
    fileSize: null,
    embeddedArtist: embeddedAudio?.artist ?? null,
    embeddedAlbum: embeddedAudio?.album ?? null,
    trackNumber: toNumber(detail.sortOrder) ?? null,
    sectionLabel: null,
    sectionKey: null,
    waveformPath: waveformFile?.path ?? null,
    libraryId: detail.parentEntityId ?? null,
    sortOrder: toNumber(detail.sortOrder) ?? 0,
    studioId: null,
    performers: [],
    tags: [],
    accessCount: toNumber(consumption?.accessCount) ?? 0,
    lastActiveAt: consumption?.lastActiveAt ?? null,
    createdAt: "",
  };
}
