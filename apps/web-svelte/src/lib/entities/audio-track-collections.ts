import type { EntityCard } from "$lib/api/generated/model";
import { getCapability } from "$lib/api/capabilities";
import { fetchEntity } from "$lib/api/entities";
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
  albumCache?: Map<string, EntityCard | null>;
  artistCache?: Map<string, EntityCard | null>;
  signal?: AbortSignal;
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
  const albumCache = options.albumCache ?? new Map<string, EntityCard | null>();
  const detail = await getCachedAudioLibrary(libraryId, albumCache, options.signal);
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
  const artistCache = options.artistCache ?? new Map<string, EntityCard | null>();
  const albumCache = options.albumCache ?? new Map<string, EntityCard | null>();
  const artist = await getCachedMusicArtist(artistId, artistCache, options.signal);
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
  options: Pick<CollectOptions, "signal"> = {},
): Promise<AudioTrackCollectionResult> {
  const albumCache = new Map<string, EntityCard | null>();
  const artistCache = new Map<string, EntityCard | null>();
  const tracks: AudioTrackListItemDto[] = [];
  const albumCoverUrls: Record<string, string | null | undefined> = {};

  for (const item of items) {
    options.signal?.throwIfAborted();
    const entity = item.entity;
    if (!entity) continue;

    if (entity.kind === ENTITY_KIND.audioTrack) {
      if (entity.isWanted === true) continue;
      const album = entity.parentEntityId
        ? await getCachedAudioLibrary(entity.parentEntityId, albumCache, options.signal)
        : null;
      if (album) albumCoverUrls[album.id] = audioLibraryCoverUrl(album);
      tracks.push(entityThumbnailToTrackItem(entity, entity.parentEntityId ?? null, {
        sectionLabel: album?.title ?? null,
        sectionKey: album ? albumSectionKey(album.id) : null,
        libraryId: album?.id ?? entity.parentEntityId ?? null,
      }));
    } else if (entity.kind === ENTITY_KIND.audioLibrary) {
      const album = await collectLibraryTracks(entity.id, {
        groupByAlbum: true,
        albumCache,
        artistCache,
        signal: options.signal,
      });
      tracks.push(...album.tracks);
      Object.assign(albumCoverUrls, album.albumCoverUrls);
    } else if (entity.kind === ENTITY_KIND.musicArtist) {
      const artist = await collectArtistTracks(entity.id, {
        groupByAlbum: true,
        albumCache,
        artistCache,
        signal: options.signal,
      });
      tracks.push(...artist.tracks);
      Object.assign(albumCoverUrls, artist.albumCoverUrls);
    }
  }

  return { tracks, albumCoverUrls };
}

export function tracksFromAudioLibraryDetail(
  detail: EntityCard,
  groupByAlbum: boolean,
): AudioTrackListItemDto[] {
  const trackGroup = detail.childrenByKind.find((group) => group.kind === ENTITY_KIND.audioTrack);
  return (trackGroup?.entities ?? [])
    .filter((thumb) => thumb.isWanted !== true)
    .map((thumb) => entityThumbnailToTrackItem(thumb, detail.id, {
      sectionLabel: groupByAlbum ? detail.title : undefined,
      sectionKey: groupByAlbum ? albumSectionKey(detail.id) : undefined,
    }))
    .sort((left, right) => left.sortOrder - right.sortOrder);
}

function audioLibraryCoverUrl(detail: EntityCard): string | null {
  const images = getCapability(detail.capabilities, CAPABILITY_KIND.images);
  return assetUrl(images?.coverUrl ?? images?.thumbnailUrl) || null;
}

async function getCachedAudioLibrary(
  id: string,
  cache: Map<string, EntityCard | null>,
  signal?: AbortSignal,
): Promise<EntityCard | null> {
  if (cache.has(id)) return cache.get(id) ?? null;
  const detail = await fetchEntity(id, { signal }).catch(() => {
    signal?.throwIfAborted();
    return null;
  });
  cache.set(id, detail);
  return detail;
}

async function getCachedMusicArtist(
  id: string,
  cache: Map<string, EntityCard | null>,
  signal?: AbortSignal,
): Promise<EntityCard | null> {
  if (cache.has(id)) return cache.get(id) ?? null;
  const detail = await fetchEntity(id, { signal }).catch(() => {
    signal?.throwIfAborted();
    return null;
  });
  cache.set(id, detail);
  return detail;
}

function albumSectionKey(albumId: string): string {
  return `album:${albumId}`;
}
