export const VIDEO_IMAGE_FORMATS = new Set([
  "h264",
  "hevc",
  "h265",
  "vp8",
  "vp9",
  "av1",
  "mpeg4",
  "mpeg2video",
  "wmv3",
  "flv1",
  "theora",
  "vp6f",
  "matroska",
  "webm",
  "mp4",
  "m4v",
  "mkv",
  "mov",
  "avi",
  "wmv",
  "flv",
]);

export const VIDEO_PREVIEW_MAX_FILE_SIZE_BYTES = 50 * 1024 * 1024;

interface VideoImageCandidate {
  isVideo?: boolean | null;
  format?: string | null;
  title?: string | null;
  previewPath?: string | null;
  fileSize?: number | null;
}

export function isVideoImageFormat(format: string | null | undefined): boolean {
  return format ? VIDEO_IMAGE_FORMATS.has(format.toLowerCase()) : false;
}

export function isVideoImage<T extends VideoImageCandidate>(image: T): boolean {
  if (image.isVideo) {
    return true;
  }

  if (isVideoImageFormat(image.format)) {
    return true;
  }

  const ext = image.title?.split(".").pop()?.toLowerCase();
  return ext ? VIDEO_IMAGE_FORMATS.has(ext) : false;
}

export function canUseInlineVideoPreview<T extends VideoImageCandidate>(image: T): boolean {
  if (!isVideoImage(image) || !image.previewPath) {
    return false;
  }

  return image.fileSize == null || image.fileSize <= VIDEO_PREVIEW_MAX_FILE_SIZE_BYTES;
}

export function formatDuration(seconds: number | null | undefined): string | null {
  if (!seconds) {
    return null;
  }

  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = Math.floor(seconds % 60);

  if (h > 0) {
    return `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
  }

  return `${m}:${String(s).padStart(2, "0")}`;
}

export function formatFileSize(bytes: number | null | undefined): string | null {
  if (!bytes) {
    return null;
  }

  const gb = bytes / (1024 * 1024 * 1024);
  if (gb >= 1) {
    return `${gb.toFixed(1)} GB`;
  }

  const mb = bytes / (1024 * 1024);
  if (mb >= 1) {
    return `${mb.toFixed(0)} MB`;
  }

  const kb = bytes / 1024;
  return `${kb.toFixed(0)} KB`;
}

export interface HlsRendition {
  name: string;
  label: string;
  height: number;
  videoBitrate: string;
  maxRate: string;
  bufferSize: string;
  audioBitrate: string;
  crf: number;
}

export const HLS_RENDITION_PRESETS: readonly HlsRendition[] = [
  {
    name: "1080p",
    label: "1080p",
    height: 1080,
    videoBitrate: "5200k",
    maxRate: "5600k",
    bufferSize: "10400k",
    audioBitrate: "160k",
    crf: 18,
  },
  {
    name: "720p",
    label: "720p",
    height: 720,
    videoBitrate: "2800k",
    maxRate: "3200k",
    bufferSize: "5600k",
    audioBitrate: "160k",
    crf: 19,
  },
  {
    name: "480p",
    label: "480p",
    height: 480,
    videoBitrate: "1400k",
    maxRate: "1600k",
    bufferSize: "2800k",
    audioBitrate: "128k",
    crf: 20,
  },
  {
    name: "360p",
    label: "360p",
    height: 360,
    videoBitrate: "850k",
    maxRate: "950k",
    bufferSize: "1700k",
    audioBitrate: "128k",
    crf: 21,
  },
  {
    name: "240p",
    label: "240p",
    height: 240,
    videoBitrate: "450k",
    maxRate: "520k",
    bufferSize: "900k",
    audioBitrate: "96k",
    crf: 22,
  },
  {
    name: "180p",
    label: "180p",
    height: 180,
    videoBitrate: "320k",
    maxRate: "360k",
    bufferSize: "640k",
    audioBitrate: "96k",
    crf: 23,
  },
];

export function getHlsRenditions(sourceHeight: number | null | undefined): HlsRendition[] {
  const height = sourceHeight ?? 720;
  const renditions = HLS_RENDITION_PRESETS.filter((preset) => preset.height <= height);

  if (renditions.length > 0) {
    return renditions.map((preset) => ({ ...preset }));
  }

  return [
    {
      name: `${height}p`,
      label: `${height}p`,
      height,
      videoBitrate: "320k",
      maxRate: "360k",
      bufferSize: "640k",
      audioBitrate: "96k",
      crf: 23,
    },
  ];
}

export type HlsPackageState = "idle" | "pending" | "ready" | "error";

export interface HlsStatus {
  state: HlsPackageState;
  renditions: HlsRendition[];
  error?: string;
}

export const HLS_RETRY_AFTER_SECONDS = 2;

export function getResolutionLabel(height: number | null | undefined): string | null {
  if (!height) {
    return null;
  }

  if (height >= 2160) {
    return "4K";
  }

  if (height >= 1080) {
    return "1080p";
  }

  if (height >= 720) {
    return "720p";
  }

  if (height >= 480) {
    return "480p";
  }

  return `${height}p`;
}
