import type {
  AudioLibraryListItemDto,
  GalleryListItemDto,
  LibraryRootSummaryDto,
} from "$lib/entities/media-view-models";
import { ENTITY_KIND } from "$lib/entities/entity-codes";

export type UploadTarget =
  | {
      kind: typeof ENTITY_KIND.video;
      libraryRootId?: string;
      videoSeriesId?: string;
      seasonNumber?: number | null;
    }
  | { kind: typeof ENTITY_KIND.image; libraryRootId?: string; galleryId?: string }
  | { kind: typeof ENTITY_KIND.audio; audioLibraryId?: string }
  | { kind: typeof ENTITY_KIND.book; libraryRootId?: string; bookId?: string };

export type UploadCategory = UploadTarget["kind"];

export interface UploadFileProgress {
  file: File;
  status: "pending" | "uploading" | "done" | "error";
  error?: string;
}

export interface UploadPickerState {
  roots: LibraryRootSummaryDto[];
  galleries: GalleryListItemDto[];
  audioLibraries: AudioLibraryListItemDto[];
}

export function categoryForTarget(target: UploadTarget): UploadCategory {
  return target.kind;
}

export function acceptForCategory(category: UploadCategory): string {
  switch (category) {
    case ENTITY_KIND.video:
      return "video/*,.mkv,.mp4,.mov,.webm,.avi,.m4v,.wmv,.flv,.ts,.mpg,.mpeg";
    case ENTITY_KIND.image:
      return "image/*,.jpg,.jpeg,.png,.webp,.gif,.avif,.bmp,.tif,.tiff";
    case ENTITY_KIND.audio:
      return "audio/*,.mp3,.flac,.m4a,.aac,.ogg,.opus,.wav,.wma";
    case ENTITY_KIND.book:
      return ".zip,.cbz,application/zip,application/octet-stream";
  }
}

export function uploadTargetLabel(target: UploadTarget): string {
  return `${categoryForTarget(target)} files`;
}

export function dragHasFiles(dt: DataTransfer | null | undefined) {
  if (!dt?.types) return false;
  for (let i = 0; i < dt.types.length; i += 1) {
    if (dt.types[i] === "Files") return true;
  }
  return false;
}
