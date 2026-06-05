type LightboxMediaImage = {
  title?: string | null;
  format?: string | null;
  previewPath?: string | null;
  fullPath?: string | null;
  thumbnailPath?: string | null;
};

export type LightboxVideoSource = {
  src: string;
  type?: string;
  quality: "original" | "fallback";
};

const FORMAT_MIME_TYPES = new Map<string, string>([
  ["mp4", "video/mp4"],
  ["m4v", "video/mp4"],
  ["h264", "video/mp4"],
  ["mpeg4", "video/mp4"],
  ["mov", "video/quicktime"],
  ["hevc", "video/quicktime"],
  ["h265", "video/quicktime"],
  ["webm", "video/webm"],
  ["vp8", "video/webm"],
  ["vp9", "video/webm"],
  ["av1", "video/webm"],
  ["mkv", "video/x-matroska"],
  ["matroska", "video/x-matroska"],
  ["avi", "video/x-msvideo"],
  ["wmv", "video/x-ms-wmv"],
  ["flv", "video/x-flv"],
]);

export function mimeTypeForImageVideoFormat(
  format: string | null | undefined,
  title: string | null | undefined,
): string | undefined {
  const normalizedFormat = format?.toLowerCase();
  if (normalizedFormat && FORMAT_MIME_TYPES.has(normalizedFormat)) {
    return FORMAT_MIME_TYPES.get(normalizedFormat);
  }

  const extension = title?.split(".").pop()?.toLowerCase();
  return extension ? FORMAT_MIME_TYPES.get(extension) : undefined;
}

export function buildLightboxVideoSources(image: LightboxMediaImage): LightboxVideoSource[] {
  const sources: LightboxVideoSource[] = [];
  const seen = new Set<string>();

  function add(src: string | null | undefined, quality: LightboxVideoSource["quality"], type?: string) {
    if (!src || seen.has(src)) return;
    seen.add(src);
    sources.push({ src, type, quality });
  }

  add(image.previewPath, "fallback", "video/mp4");
  add(image.fullPath, "original", mimeTypeForImageVideoFormat(image.format, image.title));

  if (sources.length === 0) {
    add(image.thumbnailPath, "fallback");
  }

  return sources;
}

export function buildLightboxImageSource(image: LightboxMediaImage): string | null {
  return image.fullPath ?? image.previewPath ?? image.thumbnailPath ?? null;
}
