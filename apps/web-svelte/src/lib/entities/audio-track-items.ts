import type { AudioTrackDetail, EntityThumbnail } from "$lib/api/generated/model";
import { getCapability } from "$lib/api/capabilities";
import { CAPABILITY_KIND, ENTITY_FILE_ROLE } from "$lib/entities/entity-codes";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";

export interface EntityThumbnailTrackItemOptions {
  sectionLabel?: string | null;
  sectionKey?: string | null;
  libraryId?: string | null;
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

/**
 * Build a lightweight track list item from an entity thumbnail summary.
 * Avoids the N+1 fetch pattern of calling fetchAudioTrack() per track.
 */
export function entityThumbnailToTrackItem(
  thumb: EntityThumbnail,
  libraryId: string | null,
  options: EntityThumbnailTrackItemOptions = {},
): AudioTrackListItemDto {
  const durationMeta = thumb.meta.find((m) => m.icon === "duration");
  const codecMeta = thumb.meta.find((m) => m.icon === "audio");
  const sectionMeta = thumb.meta.find((m) => m.icon === "disc");

  return {
    id: thumb.id,
    title: thumb.title,
    date: null,
    rating: toNumber(thumb.rating) ?? null,
    organized: thumb.isOrganized,
    isNsfw: thumb.isNsfw,
    duration: durationMeta ? parseDisplayDuration(durationMeta.label) : null,
    bitRate: null,
    sampleRate: null,
    channels: null,
    codec: codecMeta?.label ?? null,
    fileSize: null,
    embeddedArtist: null,
    embeddedAlbum: null,
    trackNumber: toNumber(thumb.sortOrder) ?? null,
    sectionLabel: options.sectionLabel ?? sectionMeta?.label ?? null,
    sectionKey: options.sectionKey ?? null,
    waveformPath: null,
    libraryId: options.libraryId ?? libraryId,
    sortOrder: toNumber(thumb.sortOrder) ?? 0,
    studioId: null,
    performers: [],
    tags: [],
    playCount: toNumber(thumb.playCount) ?? 0,
    lastPlayedAt: null,
    createdAt: "",
  };
}

export function audioTrackDetailToListItem(detail: AudioTrackDetail): AudioTrackListItemDto {
  const technical = getCapability(detail.capabilities, CAPABILITY_KIND.technical);
  const playback = getCapability(detail.capabilities, CAPABILITY_KIND.playback);
  const rating = getCapability(detail.capabilities, CAPABILITY_KIND.rating);
  const flags = getCapability(detail.capabilities, CAPABILITY_KIND.flags);
  const files = getCapability(detail.capabilities, CAPABILITY_KIND.files);

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
    duration: parseDurationString(technical?.duration) ?? null,
    bitRate: toNumber(technical?.bitRate) ?? null,
    sampleRate: toNumber(technical?.sampleRate) ?? null,
    channels: toNumber(technical?.channels) ?? null,
    codec: technical?.codec ?? null,
    fileSize: null,
    embeddedArtist: detail.embeddedArtist ?? null,
    embeddedAlbum: detail.embeddedAlbum ?? null,
    trackNumber: toNumber(detail.sortOrder) ?? null,
    sectionLabel: null,
    sectionKey: null,
    waveformPath: waveformFile?.path ?? null,
    libraryId: detail.parentEntityId ?? null,
    sortOrder: toNumber(detail.sortOrder) ?? 0,
    studioId: null,
    performers: [],
    tags: [],
    playCount: toNumber(playback?.playCount) ?? 0,
    lastPlayedAt: playback?.lastPlayedAt ?? null,
    createdAt: "",
  };
}
