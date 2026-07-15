import { listAudioTracks } from "$lib/api/generated/prismedia";
import type {
  EntityListResponse,
  EntityThumbnail,
  ListAudioTracksParams,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated } from "$lib/api/generated-response";
import { fetchEntityThumbnails } from "$lib/api/entities";
import { assetUrl } from "$lib/api/orval-fetch";
import { entityThumbnailToTrackItem } from "$lib/entities/audio-track-items";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";

const AUDIO_TRACK_PAGE_SIZE = 1_000;
const THUMBNAIL_BATCH_SIZE = 250;

export interface AudioTrackLibraryResult {
  tracks: AudioTrackListItemDto[];
  albumCoverUrls: Record<string, string | null>;
}

interface LoadAudioTrackLibraryOptions {
  hideNsfw: boolean;
  signal?: AbortSignal;
}

interface AudioTrackLibraryDependencies {
  listPage?: (
    params: ListAudioTracksParams,
    signal?: AbortSignal,
  ) => Promise<EntityListResponse>;
  fetchThumbnails?: (
    ids: string[],
    options: LoadAudioTrackLibraryOptions,
  ) => Promise<EntityThumbnail[]>;
}

async function listPage(
  params: ListAudioTracksParams,
  signal?: AbortSignal,
): Promise<EntityListResponse> {
  const response = await listAudioTracks(params, requestInit({ signal }));
  return unwrapGenerated(response, "Failed to load tracks");
}

async function fetchThumbnails(
  ids: string[],
  options: LoadAudioTrackLibraryOptions,
): Promise<EntityThumbnail[]> {
  return fetchEntityThumbnails(ids, options);
}

async function fetchThumbnailBatches(
  ids: string[],
  options: LoadAudioTrackLibraryOptions,
  fetchBatch: NonNullable<AudioTrackLibraryDependencies["fetchThumbnails"]>,
): Promise<EntityThumbnail[]> {
  const thumbnails: EntityThumbnail[] = [];
  for (let index = 0; index < ids.length; index += THUMBNAIL_BATCH_SIZE) {
    options.signal?.throwIfAborted();
    thumbnails.push(...await fetchBatch(ids.slice(index, index + THUMBNAIL_BATCH_SIZE), options));
  }
  return thumbnails;
}

/**
 * Load and hydrate every standalone music track in the library. Track pages are cursor-paged by
 * the API, while album and artist summaries are batch-resolved so mixed-album playback retains
 * accurate now-playing labels and artwork without an N+1 detail request per song.
 */
export async function loadAudioTrackLibrary(
  options: LoadAudioTrackLibraryOptions,
  dependencies: AudioTrackLibraryDependencies = {},
): Promise<AudioTrackLibraryResult> {
  const fetchPage = dependencies.listPage ?? listPage;
  const fetchBatch = dependencies.fetchThumbnails ?? fetchThumbnails;
  const trackThumbnails: EntityThumbnail[] = [];
  const seenTrackIds = new Set<string>();
  const seenCursors = new Set<string>();
  let cursor: string | undefined;

  do {
    options.signal?.throwIfAborted();
    const page = await fetchPage({
      cursor,
      hideNsfw: options.hideNsfw,
      limit: AUDIO_TRACK_PAGE_SIZE,
    }, options.signal);

    for (const track of page.items) {
      if (seenTrackIds.has(track.id)) continue;
      seenTrackIds.add(track.id);
      trackThumbnails.push(track);
    }

    const nextCursor = page.nextCursor ?? undefined;
    if (!nextCursor || seenCursors.has(nextCursor)) break;
    seenCursors.add(nextCursor);
    cursor = nextCursor;
  } while (cursor);

  const albumIds = [...new Set(trackThumbnails.map((track) => track.parentEntityId).filter((id): id is string => Boolean(id)))];
  const albums = await fetchThumbnailBatches(albumIds, options, fetchBatch);
  const albumById = new Map(albums.map((album) => [album.id, album]));
  const artistIds = [...new Set(albums.map((album) => album.parentEntityId).filter((id): id is string => Boolean(id)))];
  const artists = await fetchThumbnailBatches(artistIds, options, fetchBatch);
  const artistById = new Map(artists.map((artist) => [artist.id, artist]));
  const albumCoverUrls: Record<string, string | null> = {};
  const inheritedTrackArtworkByAlbum = new Map<string, string>();

  for (const track of trackThumbnails) {
    if (!track.parentEntityId || inheritedTrackArtworkByAlbum.has(track.parentEntityId)) continue;
    const artwork = assetUrl(track.coverThumb2xUrl ?? track.coverThumbUrl ?? track.coverUrl);
    if (artwork) inheritedTrackArtworkByAlbum.set(track.parentEntityId, artwork);
  }

  for (const album of albums) {
    const albumArtwork = assetUrl(
      album.coverThumb2xUrl ?? album.coverThumbUrl ?? album.coverUrl,
    );
    albumCoverUrls[album.id] = albumArtwork || inheritedTrackArtworkByAlbum.get(album.id) || null;
  }

  return {
    tracks: trackThumbnails.map((track) => {
      const album = track.parentEntityId ? albumById.get(track.parentEntityId) : undefined;
      const artist = album?.parentEntityId ? artistById.get(album.parentEntityId) : undefined;
      return entityThumbnailToTrackItem(track, album?.id ?? track.parentEntityId, {
        libraryId: album?.id ?? track.parentEntityId,
        embeddedAlbum: album?.title ?? null,
        embeddedArtist: artist?.title ?? null,
      });
    }),
    albumCoverUrls,
  };
}
