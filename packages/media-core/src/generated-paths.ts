import { existsSync } from "node:fs";
import path from "node:path";

function findWorkspaceRoot(startDir: string) {
  let current = path.resolve(startDir);

  while (true) {
    if (
      existsSync(path.join(current, "pnpm-workspace.yaml")) ||
      existsSync(path.join(current, ".git"))
    ) {
      return current;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      return null;
    }

    current = parent;
  }
}

export function resolveExistingMediaPath(filePath: string | null | undefined) {
  if (!filePath) return null;

  const candidate = path.resolve(filePath);
  return existsSync(candidate) ? candidate : null;
}

function getDefaultCacheRoots() {
  const workspaceRoot = findWorkspaceRoot(process.cwd());
  if (!workspaceRoot) {
    const localCache = path.resolve(process.cwd(), ".prismedia-cache");
    return {
      canonical: localCache,
      candidates: [localCache],
    };
  }

  const dotnetCache = path.join(workspaceRoot, "apps", "backend", "data", "cache");
  const sharedCache = path.join(workspaceRoot, ".prismedia-cache");

  return {
    canonical: sharedCache,
    candidates: [sharedCache, dotnetCache],
  };
}

export function getCacheRootDir() {
  if (process.env.PRISMEDIA_CACHE_DIR) {
    return path.resolve(process.env.PRISMEDIA_CACHE_DIR);
  }

  return getDefaultCacheRoots().canonical;
}

export function getCacheRootCandidates() {
  if (process.env.PRISMEDIA_CACHE_DIR) {
    return [path.resolve(process.env.PRISMEDIA_CACHE_DIR)];
  }

  return [...new Set(getDefaultCacheRoots().candidates)];
}

export function getGeneratedVideoDir(videoId: string) {
  return path.join(getCacheRootDir(), "videos", videoId);
}

export function getVideoSubtitlesDir(videoId: string) {
  return path.join(getGeneratedVideoDir(videoId), "subtitles");
}

export function getGeneratedPerformerDir(performerId: string) {
  return path.join(getCacheRootDir(), "performers", performerId);
}

export function getGeneratedStudioDir(studioId: string) {
  return path.join(getCacheRootDir(), "studios", studioId);
}

export function getGeneratedTagDir(tagId: string) {
  return path.join(getCacheRootDir(), "tags", tagId);
}

export function getGeneratedSeriesDir(videoSeriesId: string) {
  return path.join(getCacheRootDir(), "video-series", videoSeriesId);
}

export function getGeneratedImageDir(imageId: string) {
  return path.join(getCacheRootDir(), "images", imageId);
}

export function getGeneratedGalleryDir(galleryId: string) {
  return path.join(getCacheRootDir(), "galleries", galleryId);
}

export function getGeneratedBookPageDir(pageId: string) {
  return path.join(getCacheRootDir(), "book-pages", pageId);
}

export function getGeneratedBookChapterDir(chapterId: string) {
  return path.join(getCacheRootDir(), "book-chapters", chapterId);
}

export function getGeneratedBookVolumeDir(volumeId: string) {
  return path.join(getCacheRootDir(), "book-volumes", volumeId);
}

export function getGeneratedBookDir(bookId: string) {
  return path.join(getCacheRootDir(), "books", bookId);
}

export function getGeneratedCollectionDir(collectionId: string) {
  return path.join(getCacheRootDir(), "collections", collectionId);
}

export function getGeneratedAudioLibraryDir(libraryId: string) {
  return path.join(getCacheRootDir(), "audio-libraries", libraryId);
}

export function getGeneratedAudioTrackDir(trackId: string) {
  return path.join(getCacheRootDir(), "audio-tracks", trackId);
}

export function getSidecarPaths(videoFilePath: string) {
  const dir = path.dirname(videoFilePath);
  const stem = path.basename(videoFilePath, path.extname(videoFilePath));

  return {
    thumbnail: path.join(dir, `${stem}-thumb.jpg`),
    cardThumbnail: path.join(dir, `${stem}-card.jpg`),
    preview: path.join(dir, `${stem}-preview.mp4`),
    sprite: path.join(dir, `${stem}-sprite.jpg`),
    trickplayVtt: path.join(dir, `${stem}-trickplay.vtt`),
  };
}

export const VIDEO_GENERATED_FILENAMES = {
  thumb: "thumbnail.jpg",
  card: "card.jpg",
  sprite: "sprite.jpg",
  preview: "preview.mp4",
  trickplay: "trickplay.vtt",
} as const;

export type VideoGeneratedLayout = "dedicated" | "sidecar";

export interface VideoGeneratedDiskPaths {
  thumb: string;
  card: string;
  preview: string;
  sprite: string;
  trickplay: string;
}

export function videoGeneratedLayoutFromDedicated(dedicated: boolean): VideoGeneratedLayout {
  return dedicated ? "dedicated" : "sidecar";
}

export function getVideoGeneratedDiskPaths(
  videoId: string,
  videoFilePath: string,
  layout: VideoGeneratedLayout,
): VideoGeneratedDiskPaths {
  if (layout === "dedicated") {
    const base = getGeneratedVideoDir(videoId);
    return {
      thumb: path.join(base, VIDEO_GENERATED_FILENAMES.thumb),
      card: path.join(base, VIDEO_GENERATED_FILENAMES.card),
      preview: path.join(base, VIDEO_GENERATED_FILENAMES.preview),
      sprite: path.join(base, VIDEO_GENERATED_FILENAMES.sprite),
      trickplay: path.join(base, VIDEO_GENERATED_FILENAMES.trickplay),
    };
  }

  const sidecar = getSidecarPaths(videoFilePath);
  return {
    thumb: sidecar.thumbnail,
    card: sidecar.cardThumbnail,
    preview: sidecar.preview,
    sprite: sidecar.sprite,
    trickplay: sidecar.trickplayVtt,
  };
}

export function allVideoGeneratedDiskPaths(videoId: string, videoFilePath: string): string[] {
  const dedicated = getVideoGeneratedDiskPaths(videoId, videoFilePath, "dedicated");
  const sidecar = getVideoGeneratedDiskPaths(videoId, videoFilePath, "sidecar");
  return [...new Set([...Object.values(dedicated), ...Object.values(sidecar)])];
}
