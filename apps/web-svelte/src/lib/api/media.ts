import {
  getAudioLibrary,
  getAudioTrack,
  getBook,
  getGallery,
  getImage,
  getVideo,
  getVideoSeason,
  getVideoSeries,
  listAudioLibraries,
  listAudioTracks,
  listBooks,
  listGalleries,
  listImages,
  listVideos,
  listVideoSeries,
} from "$lib/api/generated/prismedia";
import type {
  AudioLibraryDetail,
  AudioTrackDetail,
  BookDetail,
  EntityListResponse,
  GalleryDetail,
  ImageDetail,
  VideoDetail,
  VideoSeasonDetail,
  VideoSeriesDetail,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";

export type {
  AudioLibraryDetail,
  AudioTrackDetail,
  BookDetail,
  GalleryDetail,
  ImageDetail,
  VideoDetail,
  VideoSeasonDetail,
  VideoSeriesDetail,
};

export type MediaListResponse = EntityListResponse;
export type VideoListResponse = EntityListResponse;
export type VideoSeriesListResponse = EntityListResponse;

export function fetchVideos(options?: RequestOptions): Promise<VideoListResponse> {
  return listVideos(undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to load videos"),
  );
}

export function fetchVideo(id: string, options?: RequestOptions): Promise<VideoDetail> {
  return getVideo(id, undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to fetch video ${id}`),
  );
}

export function fetchSeriesList(options?: RequestOptions): Promise<VideoSeriesListResponse> {
  return listVideoSeries(undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to load series"),
  );
}

export function fetchSeries(
  id: string,
  options?: RequestOptions,
): Promise<VideoSeriesDetail> {
  return getVideoSeries(id, undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to fetch series ${id}`),
  );
}

export function fetchSeason(
  seriesId: string,
  seasonId: string,
  options?: RequestOptions,
): Promise<VideoSeasonDetail> {
  return getVideoSeason(seriesId, seasonId, undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to fetch season ${seasonId}`),
  );
}

export function fetchImages(options?: RequestOptions): Promise<MediaListResponse> {
  return listImages(undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to load images"),
  );
}

export function fetchGalleries(options?: RequestOptions): Promise<MediaListResponse> {
  return listGalleries(undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to load galleries"),
  );
}

export function fetchBooks(options?: RequestOptions): Promise<MediaListResponse> {
  return listBooks(undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to load books"),
  );
}

export function fetchAudioLibraries(options?: RequestOptions): Promise<MediaListResponse> {
  return listAudioLibraries(undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to load audio libraries"),
  );
}

export function fetchAudioTracks(options?: RequestOptions): Promise<MediaListResponse> {
  return listAudioTracks(undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, "Failed to load audio tracks"),
  );
}

export function fetchImage(id: string, options?: RequestOptions): Promise<ImageDetail> {
  return getImage(id, undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to fetch image ${id}`),
  );
}

export function fetchGallery(id: string, options?: RequestOptions): Promise<GalleryDetail> {
  return getGallery(id, undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to fetch gallery ${id}`),
  );
}

export function fetchBook(id: string, options?: RequestOptions): Promise<BookDetail> {
  return getBook(id, undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to fetch book ${id}`),
  );
}

export function fetchAudioLibrary(
  id: string,
  options?: RequestOptions,
): Promise<AudioLibraryDetail> {
  return getAudioLibrary(id, undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to fetch audio library ${id}`),
  );
}

export function fetchAudioTrack(
  id: string,
  options?: RequestOptions,
): Promise<AudioTrackDetail> {
  return getAudioTrack(id, undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to fetch audio track ${id}`),
  );
}
