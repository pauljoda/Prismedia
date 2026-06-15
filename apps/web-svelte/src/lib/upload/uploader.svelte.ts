import { invalidateAll } from "$app/navigation";
import type {
  AudioLibraryListItemDto,
  LibraryRootSummaryDto,
} from "$lib/entities/media-view-models";
import { fetchApi as fetchApi, uploadFile as uploadFile } from "$lib/api/orval-fetch";
import type { UploadFileProgress, UploadTarget } from "./upload-types";

interface CreateUploaderOptions {
  target: UploadTarget;
  onUploaded?: () => void | Promise<void>;
}

type ExplicitDestination = {
  rootId?: string;
  audioLibraryId?: string;
};

export class Uploader {
  files = $state<UploadFileProgress[]>([]);
  isUploading = $state(false);
  candidateRoots = $state.raw<LibraryRootSummaryDto[]>([]);
  candidateAudioLibraries = $state.raw<AudioLibraryListItemDto[]>([]);
  private pendingFiles: File[] | null = null;
  private resolvedRootId: string | null = null;
  private resolvedAudioLibraryId: string | null = null;

  constructor(private options: CreateUploaderOptions) {}

  get needsRootPicker() {
    return this.candidateRoots.length > 0;
  }

  get needsAudioLibraryPicker() {
    return this.candidateAudioLibraries.length > 0;
  }

  resetState = () => {
    this.files = [];
  };

  uploadFiles = async (fileList: FileList | File[]) => {
    const files = Array.from(fileList);
    if (files.length === 0) return;
    const { target } = this.options;

    if (target.kind === "video" && !target.videoSeriesId && !target.libraryRootId) {
      const roots = await this.loadVideoRoots(files);
      if (!roots) return;
      if (roots.length === 1) {
        this.resolvedRootId = roots[0]!.id;
        await this.runUploads(files, { rootId: roots[0]!.id });
        return;
      }
      this.pendingFiles = files;
      this.candidateRoots = roots;
      return;
    }

    if (target.kind === "image" && !target.galleryId && !target.libraryRootId) {
      const roots = await this.loadImageRoots(files);
      if (!roots) return;
      if (roots.length === 1) {
        this.resolvedRootId = roots[0]!.id;
        await this.runUploads(files, { rootId: roots[0]!.id });
        return;
      }
      this.pendingFiles = files;
      this.candidateRoots = roots;
      return;
    }

    if (target.kind === "audio" && !target.audioLibraryId) {
      const libraries = await this.loadAudioLibraries(files);
      if (!libraries) return;
      if (libraries.length === 1) {
        this.resolvedAudioLibraryId = libraries[0]!.id;
        await this.runUploads(files, { audioLibraryId: libraries[0]!.id });
        return;
      }
      this.pendingFiles = files;
      this.candidateAudioLibraries = libraries;
      return;
    }

    if (target.kind === "book" && !target.bookId && !target.libraryRootId) {
      const roots = await this.loadBookRoots(files);
      if (!roots) return;
      if (roots.length === 1) {
        this.resolvedRootId = roots[0]!.id;
        await this.runUploads(files, { rootId: roots[0]!.id });
        return;
      }
      this.pendingFiles = files;
      this.candidateRoots = roots;
      return;
    }

    await this.runUploads(files);
  };

  confirmRootPick = async (rootId: string) => {
    const pending = this.consumePending();
    this.candidateRoots = [];
    this.resolvedRootId = rootId;
    if (pending) await this.runUploads(pending, { rootId });
  };

  cancelRootPick = () => {
    this.pendingFiles = null;
    this.candidateRoots = [];
    this.files = [];
  };

  confirmAudioLibraryPick = async (audioLibraryId: string) => {
    const pending = this.consumePending();
    this.candidateAudioLibraries = [];
    this.resolvedAudioLibraryId = audioLibraryId;
    if (pending) await this.runUploads(pending, { audioLibraryId });
  };

  cancelAudioLibraryPick = () => {
    this.pendingFiles = null;
    this.candidateAudioLibraries = [];
    this.files = [];
  };

  private consumePending() {
    const pending = this.pendingFiles;
    this.pendingFiles = null;
    return pending;
  }

  private async loadVideoRoots(files: File[]) {
    try {
      const resp = await fetchApi<{ roots: LibraryRootSummaryDto[] }>(
        "/libraries?scanVideos=true&enabled=true",
      );
      const roots = resp.roots ?? [];
      if (roots.length === 0) {
        this.failAll(files, "No enabled video library root can receive uploads");
        return null;
      }
      return roots;
    } catch (error) {
      this.failAll(files, error instanceof Error ? error.message : "Could not load libraries");
      return null;
    }
  }

  private async loadImageRoots(files: File[]) {
    try {
      const resp = await fetchApi<{ roots: LibraryRootSummaryDto[] }>(
        "/libraries?scanImages=true&enabled=true",
      );
      const roots = resp.roots ?? [];
      if (roots.length === 0) {
        this.failAll(files, "No enabled image library root can receive uploads");
        return null;
      }
      return roots;
    } catch (error) {
      this.failAll(files, error instanceof Error ? error.message : "Could not load libraries");
      return null;
    }
  }

  private async loadAudioLibraries(files: File[]) {
    try {
      const resp = await fetchApi<{ items: AudioLibraryListItemDto[] }>(
        "/audio-libraries?limit=500",
      );
      const libraries = resp.items ?? [];
      if (libraries.length === 0) {
        this.failAll(files, "No audio library can receive track uploads");
        return null;
      }
      return libraries;
    } catch (error) {
      this.failAll(files, error instanceof Error ? error.message : "Could not load audio libraries");
      return null;
    }
  }

  private async loadBookRoots(files: File[]) {
    try {
      const resp = await fetchApi<{ roots: LibraryRootSummaryDto[] }>(
        "/libraries?scanBooks=true&enabled=true",
      );
      const roots = resp.roots ?? [];
      if (roots.length === 0) {
        this.failAll(files, "No enabled book library root can receive uploads");
        return null;
      }
      return roots;
    } catch (error) {
      this.failAll(files, error instanceof Error ? error.message : "Could not load libraries");
      return null;
    }
  }

  private failAll(files: File[], error: string) {
    this.files = files.map((file) => ({ file, status: "error", error }));
  }

  private async runUploads(files: File[], explicit: ExplicitDestination = {}) {
    this.isUploading = true;
    this.files = files.map((file) => ({ file, status: "pending" }));

    for (let index = 0; index < files.length; index += 1) {
      const file = files[index]!;
      this.files = this.files.map((entry, i) =>
        i === index ? { ...entry, status: "uploading" } : entry,
      );

      try {
        await this.uploadOne(file, explicit);
        this.files = this.files.map((entry, i) =>
          i === index ? { ...entry, status: "done" } : entry,
        );
      } catch (error) {
        this.files = this.files.map((entry, i) =>
          i === index
            ? {
                ...entry,
                status: "error",
                error: error instanceof Error ? error.message : "Upload failed",
              }
            : entry,
        );
      }
    }

    this.isUploading = false;
    await this.options.onUploaded?.();
    await invalidateAll();
  }

  private async uploadOne(file: File, explicit: ExplicitDestination) {
    const { target } = this.options;
    if (target.kind === "video") {
      if (target.videoSeriesId) {
        await uploadFile("/videos/upload", file, {
          seriesId: target.videoSeriesId,
          ...(target.seasonNumber != null
            ? { seasonNumber: String(target.seasonNumber) }
            : {}),
        });
        return;
      }
      const libraryRootId = explicit.rootId ?? target.libraryRootId ?? this.resolvedRootId;
      if (!libraryRootId) throw new Error("No library root selected for video upload");
      await uploadFile("/videos/upload", file, { libraryRootId });
      return;
    }

    if (target.kind === "image") {
      if (target.galleryId) {
        await uploadFile(`/galleries/${target.galleryId}/images/upload`, file);
        return;
      }
      const libraryRootId = explicit.rootId ?? target.libraryRootId ?? this.resolvedRootId;
      if (!libraryRootId) throw new Error("No library root selected for image upload");
      await uploadFile("/images/upload", file, { libraryRootId });
      return;
    }

    if (target.kind === "book") {
      if (target.bookId) {
        await uploadFile(`/books/${target.bookId}/chapters/upload`, file);
        return;
      }
      const libraryRootId = explicit.rootId ?? target.libraryRootId ?? this.resolvedRootId;
      if (!libraryRootId) throw new Error("No library root selected for book upload");
      await uploadFile("/books/upload", file, { libraryRootId });
      return;
    }

    const audioLibraryId =
      explicit.audioLibraryId ?? target.audioLibraryId ?? this.resolvedAudioLibraryId;
    if (!audioLibraryId) throw new Error("No audio library selected for audio upload");
    await uploadFile(`/audio-libraries/${audioLibraryId}/tracks/upload`, file);
  }
}

export function createUploader(options: CreateUploaderOptions) {
  return new Uploader(options);
}
