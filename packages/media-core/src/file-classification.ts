import path from "node:path";

export interface LibraryRoot {
  id: string;
  path: string;
  enabled: boolean;
}

export const supportedVideoExtensions = new Set([
  ".mp4",
  ".m4v",
  ".mkv",
  ".mov",
  ".webm",
  ".avi",
  ".wmv",
  ".flv",
  ".ts",
  ".m2ts",
  ".mpg",
  ".mpeg",
]);

export const supportedImageExtensions = new Set([
  ".jpg",
  ".jpeg",
  ".png",
  ".webp",
  ".gif",
  ".avif",
  ".heic",
  ".bmp",
  ".tiff",
  ".tif",
]);

/** Video/animated formats that can appear as gallery items. */
export const supportedAnimatedExtensions = new Set([
  ".webm",
  ".mp4",
  ".m4v",
  ".mkv",
  ".mov",
  ".avi",
  ".wmv",
  ".flv",
]);

export const supportedGalleryMediaExtensions = new Set([
  ...supportedImageExtensions,
  ...supportedAnimatedExtensions,
]);

export const supportedZipExtensions = new Set([".zip", ".cbz"]);

export const supportedAudioExtensions = new Set([
  ".mp3",
  ".flac",
  ".wav",
  ".ogg",
  ".aac",
  ".m4a",
  ".wma",
  ".opus",
  ".aiff",
  ".aif",
  ".alac",
  ".ape",
  ".dsf",
  ".dff",
  ".wv",
]);

/**
 * Filename patterns that mark generated/preview files we should skip during scan.
 */
export const generatedSuffixPattern = /[-._](preview|thumb|sprite|sample)$/i;

export function isVideoFile(filePath: string) {
  const ext = path.extname(filePath).toLowerCase();
  if (!supportedVideoExtensions.has(ext)) return false;
  return !generatedSuffixPattern.test(path.basename(filePath, ext));
}

export function isImageFile(filePath: string): boolean {
  const ext = path.extname(filePath).toLowerCase();
  if (!supportedGalleryMediaExtensions.has(ext)) return false;
  return !generatedSuffixPattern.test(path.basename(filePath, ext));
}

export function isAudioFile(filePath: string): boolean {
  const ext = path.extname(filePath).toLowerCase();
  if (!supportedAudioExtensions.has(ext)) return false;
  return !generatedSuffixPattern.test(path.basename(filePath, ext));
}

/** Check if a gallery media file is an animated/video format rather than a static image. */
export function isAnimatedFormat(filePath: string): boolean {
  const ext = path.extname(filePath).toLowerCase();
  return supportedAnimatedExtensions.has(ext) || ext === ".gif";
}

/** Common HTML entities that appear in filenames downloaded from websites. */
const htmlEntities: Record<string, string> = {
  "&amp;": "&",
  "&lt;": "<",
  "&gt;": ">",
  "&quot;": '"',
  "&#39;": "'",
  "&apos;": "'",
  "&#x27;": "'",
  "&#x2F;": "/",
  "&nbsp;": " ",
};

const htmlEntityPattern = new RegExp(Object.keys(htmlEntities).join("|"), "gi");

export function fileNameToTitle(filePath: string) {
  return path
    .basename(filePath, path.extname(filePath))
    .replace(/[._-]+/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .replace(htmlEntityPattern, (match) => htmlEntities[match.toLowerCase()] ?? match);
}
