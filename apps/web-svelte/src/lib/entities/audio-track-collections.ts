import type { AudioLibraryDetail, MusicArtistDetail } from "$lib/api/generated/model";
import { getCapability } from "$lib/api/capabilities";
import { fetchAudioLibrary, fetchMusicArtist } from "$lib/api/media";
import { assetUrl } from "$lib/api/orval-fetch";
import type { CollectionItem } from "$lib/collections/models";
import { CAPABILITY_KIND, ENTITY_KIND } from "$lib/entities/entity-codes";
import { entityThumbnailToTrackItem } from "$lib/entities/audio-track-items";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";

export interface AudioTrackCollectionResult {
  tracks: AudioTrackListItemDto[];
  albumCoverUrls: Record<string, string | null | undefined>;
}

interface CollectOptions {
  groupByAlbum?: boolean;
  albumCache?: Map<string, AudioLibraryDetail | null>;
  artistCache?: Map<string, MusicArtistDetail | null>;
}

export function isAudioCollectionMemberKind(kind: string): boolean {
  return kind === ENTITY_KIND.audioTrack ||
    kind === ENTITY_KIND.audioLibrary ||
    kind === ENTITY_KIND.musicArtist;
}

export async function collectLibraryTracks(
  libraryId: string,
  options: CollectOptions = {},
): Promise<AudioTrackCollectionResult> {
  const albumCache = options.albumCache ?? new Map<string, AudioLibraryDetail | null>();
  const detail = await getCachedAudioLibrary(libraryId, albumCache);
  if (!detail) return { tracks: [], albumCoverUrls: {} };

  const tracks = tracksFromAudioLibraryDetail(detail, options.groupByAlbum === true);
  const albumCoverUrls: Record<string, string | null | undefined> = {
    [detail.id]: audioLibraryCoverUrl(detail),
  };
  const subLibraryIds = detail.childrenByKind
    .filter((group) => group.kind === ENTITY_KIND.audioLibrary)
    .flatMap((group) => group.entities.map((entity) => entity.id));

  for (const childId of subLibraryIds) {
    const child = await collectLibraryTracks(childId, { ...options, albumCache });
    tracks.push(...child.tracks);
    Object.assign(albumCoverUrls, child.albumCoverUrls);
  }

  return { tracks, albumCoverUrls };
}

export async function collectArtistTracks(
  artistId: string,
  options: CollectOptions = {},
): Promise<AudioTrackCollectionResult> {
  const artistCache = options.artistCache ?? new Map<string, MusicArtistDetail | null>();
  const albumCache = options.albumCache ?? new Map<string, AudioLibraryDetail | null>();
  const artist = await getCachedMusicArtist(artistId, artistCache);
  if (!artist) return { tracks: [], albumCoverUrls: {} };

  const albumIds = artist.childrenByKind
    .filter((group) => group.kind === ENTITY_KIND.audioLibrary)
    .flatMap((group) => group.entities)
    .sort((left, right) => Number(left.sortOrder ?? 0) - Number(right.sortOrder ?? 0) || left.title.localeCompare(right.title))
    .map((album) => album.id);
  const tracks: AudioTrackListItemDto[] = [];
  const albumCoverUrls: Record<string, string | null | undefined> = {};

  for (const albumId of albumIds) {
    const album = await collectLibraryTracks(albumId, { ...options, albumCache, artistCache });
    tracks.push(...album.tracks);
    Object.assign(albumCoverUrls, album.albumCoverUrls);
  }

  return { tracks, albumCoverUrls };
}

export async function collectCollectionAudioTracks(
  items: CollectionItem[],
): Promise<AudioTrackCollectionResult> {
  const albumCache = new Map<string, AudioLibraryDetail | null>();
  const artistCache = new Map<string, MusicArtistDetail | null>();
  const tracks: AudioTrackListItemDto[] = [];
  const albumCoverUrls: Record<string, string | null | undefined> = {};

  for (const item of items) {
    const entity = item.entity;
    if (!entity) continue;

    if (entity.kind === ENTITY_KIND.audioTrack) {
      const album = entity.parentEntityId ? await getCachedAudioLibrary(entity.parentEntityId, albumCache) : null;
      if (album) albumCoverUrls[album.id] = audioLibraryCoverUrl(album);
      tracks.push(entityThumbnailToTrackItem(entity, entity.parentEntityId ?? null, {
        sectionLabel: album?.title ?? null,
        sectionKey: album ? albumSectionKey(album.id) : null,
        libraryId: album?.id ?? entity.parentEntityId ?? null,
      }));
    } else if (entity.kind === ENTITY_KIND.audioLibrary) {
      const album = await collectLibraryTracks(entity.id, { groupByAlbum: true, albumCache, artistCache });
      tracks.push(...album.tracks);
      Object.assign(albumCoverUrls, album.albumCoverUrls);
    } else if (entity.kind === ENTITY_KIND.musicArtist) {
      const artist = await collectArtistTracks(entity.id, { groupByAlbum: true, albumCache, artistCache });
      tracks.push(...artist.tracks);
      Object.assign(albumCoverUrls, artist.albumCoverUrls);
    }
  }

  return { tracks, albumCoverUrls };
}

function tracksFromAudioLibraryDetail(
  detail: AudioLibraryDetail,
  groupByAlbum: boolean,
): AudioTrackListItemDto[] {
  const trackGroup = detail.childrenByKind.find((group) => group.kind === ENTITY_KIND.audioTrack);
  return (trackGroup?.entities ?? [])
    .map((thumb) => entityThumbnailToTrackItem(thumb, detail.id, {
      sectionLabel: groupByAlbum ? detail.title : undefined,
      sectionKey: groupByAlbum ? albumSectionKey(detail.id) : undefined,
    }))
    .sort((left, right) => left.sortOrder - right.sortOrder);
}

function audioLibraryCoverUrl(detail: AudioLibraryDetail): string | null {
  const images = getCapability(detail.capabilities, CAPABILITY_KIND.images);
  return assetUrl(images?.coverUrl ?? images?.thumbnailUrl) || null;
}

async function getCachedAudioLibrary(
  id: string,
  cache: Map<string, AudioLibraryDetail | null>,
): Promise<AudioLibraryDetail | null> {
  if (cache.has(id)) return cache.get(id) ?? null;
  const detail = await fetchAudioLibrary(id).catch(() => null);
  cache.set(id, detail);
  return detail;
}

async function getCachedMusicArtist(
  id: string,
  cache: Map<string, MusicArtistDetail | null>,
): Promise<MusicArtistDetail | null> {
  if (cache.has(id)) return cache.get(id) ?? null;
  const detail = await fetchMusicArtist(id).catch(() => null);
  cache.set(id, detail);
  return detail;
}

function albumSectionKey(albumId: string): string {
  return `album:${albumId}`;
}
