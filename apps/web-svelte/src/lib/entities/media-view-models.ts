import type { GalleryTypeCode } from "$lib/api/generated/codes";

export interface TagEmbedDto {
  id: string;
  name: string;
}

export interface LibraryRootSummaryDto {
  id: string;
  path: string;
  label: string;
  enabled: boolean;
  scanVideos: boolean;
  scanImages: boolean;
  scanAudio: boolean;
  scanBooks: boolean;
}

export type GalleryType = GalleryTypeCode;

export interface GalleryListItemDto {
  id: string;
  title: string;
  galleryType: GalleryType;
  isComic: boolean;
  readCompleted: boolean;
  coverImagePath: string | null;
  previewImagePaths: string[];
  coverAspectRatio: number | null;
  childCount: number;
  imageCount: number;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  date: string | null;
  studioId: string | null;
  studioName: string | null;
  performers: { id: string; name: string }[];
  tags: TagEmbedDto[];
  parentId: string | null;
  createdAt: string;
}

export interface AudioLibraryListItemDto {
  id: string;
  title: string;
  coverImagePath: string | null;
  iconPath: string | null;
  trackCount: number;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  date: string | null;
  studioId: string | null;
  studioName: string | null;
  performers: { id: string; name: string }[];
  tags: TagEmbedDto[];
  parentId: string | null;
  createdAt: string;
}

export interface AudioTrackListItemDto {
  id: string;
  title: string;
  date: string | null;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  /** Provider-backed placeholder has not been fulfilled by a source file yet. */
  isWanted?: boolean;
  /** Server-projected source ownership; false distinguishes metadata tracks from playable files. */
  hasSourceMedia?: boolean;
  /** Latest acquisition state for a still-wanted track. */
  wantedStatus?: string | null;
  /** Latest acquisition state even after a track becomes source-backed. */
  latestAcquisitionStatus?: string | null;
  duration: number | null;
  bitRate: number | null;
  sampleRate: number | null;
  channels: number | null;
  codec: string | null;
  fileSize: number | null;
  embeddedArtist: string | null;
  embeddedAlbum: string | null;
  trackNumber: number | null;
  sectionLabel: string | null;
  sectionKey?: string | null;
  waveformPath: string | null;
  libraryId: string | null;
  sortOrder: number;
  studioId: string | null;
  performers: { id: string; name: string }[];
  tags: TagEmbedDto[];
  playCount: number;
  lastPlayedAt: string | null;
  createdAt: string;
}

export interface ImageListItemDto {
  id: string;
  title: string;
  date: string | null;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  width: number | null;
  height: number | null;
  format: string | null;
  isVideo: boolean;
  fileSize: number | null;
  thumbnailPath: string | null;
  previewPath: string | null;
  fullPath: string | null;
  galleryId: string | null;
  sortOrder: number;
  studioId: string | null;
  performers: { id: string; name: string }[];
  tags: TagEmbedDto[];
  createdAt: string;
}

export interface BookPageDto {
  id: string;
  bookId: string;
  chapterId: string;
  title: string;
  width: number | null;
  height: number | null;
  format: string | null;
  thumbnailPath: string | null;
  fullPath: string;
  sortOrder: number;
}

export interface BookChapterDto {
  id: string;
  bookId: string;
  volumeId: string | null;
  title: string;
  chapterNumber: number;
  archivePath: string;
  relativePath: string;
  pageCount: number;
  coverPageId: string | null;
  coverImagePath: string | null;
  hasCustomCover: boolean;
  pages: BookPageDto[];
}

export interface BookProgressDto {
  bookId: string;
  chapterId: string | null;
  pageIndex: number;
  pageCount: number;
  readerMode: "paged" | "webtoon";
  completedAt: string | null;
  updatedAt: string;
}

export interface VideoFileInfoModel {
  filePath?: string | null;
  streamUrl?: string | null;
  directStreamUrl?: string | null;
  fileSizeFormatted?: string | null;
  codec?: string | null;
  container?: string | null;
  width?: number | null;
  height?: number | null;
  durationFormatted?: string | null;
  bitRate?: number | null;
  frameRate?: number | null;
}
