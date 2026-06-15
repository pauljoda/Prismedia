import type {
  AudioLibraryListItemDto,
  GalleryListItemDto,
  LibraryRootSummaryDto,
} from "$lib/entities/media-view-models";

export type UploadTarget =
  | { kind: "video"; libraryRootId?: string; videoSeriesId?: string; seasonNumber?: number | null }
  | { kind: "image"; libraryRootId?: string; galleryId?: string }
  | { kind: "audio"; audioLibraryId?: string }
  | { kind: "book"; libraryRootId?: string; bookId?: string };

export type UploadCategory = "video" | "image" | "audio" | "book";

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
  switch (target.kind) {
    case "video":
      return "video";
    case "image":
      return "image";
    case "audio":
      return "audio";
    case "book":
      return "book";
  }
}

export function acceptForCategory(category: UploadCategory): string {
  switch (category) {
    case "video":
      return "video/*,.mkv,.mp4,.mov,.webm,.avi,.m4v,.wmv,.flv,.ts,.mpg,.mpeg";
    case "image":
      return "image/*,.jpg,.jpeg,.png,.webp,.gif,.avif,.bmp,.tif,.tiff";
    case "audio":
      return "audio/*,.mp3,.flac,.m4a,.aac,.ogg,.opus,.wav,.wma";
    case "book":
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
