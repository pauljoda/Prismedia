import type { AudioTrackListItemDto } from "@prismedia/contracts";
import type { AudioTrackDetail } from "$lib/api/generated/model";
import { getCapability } from "$lib/api/capabilities";

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

function toNumber(value: number | string | null | undefined): number | null {
  if (value == null) return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

export function audioTrackDetailToListItem(detail: AudioTrackDetail): AudioTrackListItemDto {
  const technical = getCapability(detail.capabilities, "technical");
  const playback = getCapability(detail.capabilities, "playback");
  const rating = getCapability(detail.capabilities, "rating");
  const flags = getCapability(detail.capabilities, "flags");
  const files = getCapability(detail.capabilities, "files");

  const waveformFile = files?.items.find(
    (f) => typeof f.role === "string" && f.role.toLowerCase() === "waveform",
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
