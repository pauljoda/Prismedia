import {
  getAudioLibrary,
  getAudioTrack,
  getBook,
  getBookAuthor,
  getGallery,
  getImage,
  getMusicArtist,
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
import { orvalFetch } from "$lib/api/orval-fetch";
import type {
  AudioLibraryDetail,
  AudioTrackDetail,
  BookAuthorDetail,
  BookDetail,
  EntityListResponse,
  GalleryDetail,
  ImageDetail,
  MovieDetail,
  MusicArtistDetail,
  VideoDetail,
  VideoSeasonDetail,
  VideoSeriesDetail,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";

export type {
  AudioLibraryDetail,
  AudioTrackDetail,
  BookAuthorDetail,
  BookDetail,
  GalleryDetail,
  ImageDetail,
  MovieDetail,
  MusicArtistDetail,
  VideoDetail,
  VideoSeasonDetail,
  VideoSeriesDetail,
};

export type MediaListResponse = EntityListResponse;
export type VideoListResponse = EntityListResponse;
export type MovieListResponse = EntityListResponse;
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

export function fetchMovies(options?: RequestOptions): Promise<MovieListResponse> {
  return orvalFetch<{ data: EntityListResponse; status: number }>(
    "/api/movies",
    requestInit(options),
  ).then((response) => unwrapGenerated(response, "Failed to load movies"));
}

export function fetchMovie(id: string, options?: RequestOptions): Promise<MovieDetail> {
  return orvalFetch<{ data: MovieDetail; status: number }>(
    `/api/movies/${id}`,
    requestInit(options),
  ).then((response) => unwrapGenerated(response, `Failed to fetch movie ${id}`));
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

export function fetchMusicArtist(
  id: string,
  options?: RequestOptions,
): Promise<MusicArtistDetail> {
  return getMusicArtist(id, undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to fetch artist ${id}`),
  );
}

export function fetchBookAuthor(
  id: string,
  options?: RequestOptions,
): Promise<BookAuthorDetail> {
  return getBookAuthor(id, undefined, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to fetch author ${id}`),
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
