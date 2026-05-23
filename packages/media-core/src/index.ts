import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { createReadStream } from "node:fs";
import { readdir, readFile, stat, open, writeFile } from "node:fs/promises";
import { spawn } from "node:child_process";
import path from "node:path";
import AdmZip from "adm-zip";

export const supportedFingerprintKinds = [
  "md5",
  "oshash",
  "image-phash",
  "video-phash",
] as const;

export type FingerprintKind = (typeof supportedFingerprintKinds)[number];

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

interface FfprobeStream {
  codec_name?: string;
  codec_type?: string;
  width?: number;
  height?: number;
  avg_frame_rate?: string;
  sample_rate?: string;
  channels?: number;
}

interface FfprobeFormat {
  duration?: string;
  size?: string;
  bit_rate?: string;
  format_name?: string;
}

interface FfprobeResult {
  streams?: FfprobeStream[];
  format?: FfprobeFormat;
}

export interface ProbeAudioMetadata {
  codec: string | null;
  sampleRate: number | null;
  channels: number | null;
}

export interface ProbeVideoMetadata {
  filePath: string;
  fileName: string;
  duration: number | null;
  fileSize: number | null;
  bitRate: number | null;
  width: number | null;
  height: number | null;
  frameRate: number | null;
  codec: string | null;
  container: string | null;
  audio: ProbeAudioMetadata | null;
}

/**
 * Filename patterns that mark generated/preview files we should skip during scan.
 * Matches stems ending with separators followed by these words, e.g.:
 *   scene.preview.mp4, scene-preview.mp4, scene_preview.mp4
 */
const generatedSuffixPattern = /[-._](preview|thumb|sprite|sample)$/i;

export function isVideoFile(filePath: string) {
  const ext = path.extname(filePath).toLowerCase();
  if (!supportedVideoExtensions.has(ext)) return false;

  // Skip generated preview/thumbnail/sample files
  const stem = path.basename(filePath, ext);
  return !generatedSuffixPattern.test(stem);
}

/** Common HTML entities that appear in filenames downloaded from websites. */
const htmlEntities: Record<string, string> = {
  "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": '"',
  "&#39;": "'", "&apos;": "'", "&#x27;": "'", "&#x2F;": "/",
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

function bookVolumeNumberKey(value: string | number): string {
  if (typeof value === "number" && Number.isFinite(value)) return String(Math.round(value));
  return String(value).trim();
}

function numericBookVolume(value: string | number): number | null {
  const key = bookVolumeNumberKey(value);
  if (!/^\d+$/.test(key)) return null;
  const parsed = Number.parseInt(key, 10);
  return Number.isSafeInteger(parsed) ? parsed : null;
}

function normalizedVolumeOnlyTitle(value: string): number | null {
  const match = value.trim().match(/^(?:volume|vol\.?|v|book)\s*0*([0-9]+)$/i);
  if (!match) return null;
  const parsed = Number.parseInt(match[1] ?? "", 10);
  return Number.isSafeInteger(parsed) ? parsed : null;
}

export function bookVolumeFolderName(volumeNumber: string | number, title?: string | null): string {
  const numeric = numericBookVolume(volumeNumber);
  const key = bookVolumeNumberKey(volumeNumber);
  const base = numeric != null
    ? `Volume ${String(numeric).padStart(2, "0")}`
    : `Volume ${key}`;
  const trimmedTitle = title?.trim() ?? "";
  const redundantTitle =
    trimmedTitle.length > 0 &&
    numeric != null &&
    normalizedVolumeOnlyTitle(trimmedTitle) === numeric;
  const suffix =
    trimmedTitle && !redundantTitle && trimmedTitle.toLowerCase() !== base.toLowerCase()
      ? ` - ${trimmedTitle}`
      : "";
  return `${base}${suffix}`
    .replace(/[/:\\]/g, " ")
    .replace(/\s+/g, " ")
    .slice(0, 160)
    .trim();
}

export function duplicatedBookVolumeFolderNameRepair(folderName: string): string | null {
  const trimmed = folderName.trim();
  const match = trimmed.match(/^volume\s+0*([0-9]+)\s+-\s+(.+)$/i);
  if (!match) return null;
  const volumeNumber = Number.parseInt(match[1] ?? "", 10);
  if (!Number.isSafeInteger(volumeNumber)) return null;
  if (normalizedVolumeOnlyTitle(match[2] ?? "") !== volumeNumber) return null;
  const canonical = bookVolumeFolderName(volumeNumber);
  return canonical === trimmed ? null : canonical;
}

export async function runProcess(
  command: string,
  args: string[],
  options?: { cwd?: string }
) {
  return new Promise<{ stdout: string; stderr: string }>((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: options?.cwd,
      stdio: ["ignore", "pipe", "pipe"],
    });

    let stdout = "";
    let stderr = "";

    child.stdout.on("data", (chunk) => {
      stdout += chunk.toString();
    });

    child.stderr.on("data", (chunk) => {
      stderr += chunk.toString();
    });

    child.on("error", reject);
    child.on("close", (code) => {
      if (code === 0) {
        resolve({ stdout, stderr });
        return;
      }

      reject(
        new Error(
          `${command} exited with code ${code ?? "unknown"}${
            stderr ? `: ${stderr.trim()}` : ""
          }`
        )
      );
    });
  });
}

/**
 * Error thrown when ffprobe/ffmpeg can't read a source file because it is
 * truncated, has no valid container header, or otherwise has no decodable
 * streams. These files are genuinely broken on disk — retrying won't help,
 * so processors should log a warning and skip rather than fail the job.
 */
export class CorruptMediaError extends Error {
  filePath: string;
  cause?: Error;
  constructor(filePath: string, cause?: Error) {
    super(
      `Media file is corrupt or unreadable: ${filePath}${
        cause?.message ? ` — ${cause.message}` : ""
      }`,
    );
    this.name = "CorruptMediaError";
    this.filePath = filePath;
    this.cause = cause;
  }
}

/**
 * Recognize ffprobe/ffmpeg error output that indicates the source file is
 * structurally broken (missing moov atom, truncated, unreadable streams, etc.).
 * We treat these as non-retryable and skip the affected processor step.
 */
export function isCorruptMediaError(err: unknown): boolean {
  if (!(err instanceof Error)) return false;
  const message = err.message ?? "";
  return (
    /moov atom not found/i.test(message) ||
    /Invalid data found when processing input/i.test(message) ||
    /Invalid argument/i.test(message) && /ffprobe|ffmpeg/i.test(message) ||
    /End of file/i.test(message) && /ffprobe|ffmpeg/i.test(message) ||
    /could not find codec parameters/i.test(message)
  );
}

export function parseFrameRate(value?: string): number | null {
  if (!value) return null;
  const [numerator, denominator] = value.split("/").map(Number);
  if (!Number.isFinite(numerator) || !Number.isFinite(denominator) || denominator === 0) {
    return null;
  }

  return Number((numerator / denominator).toFixed(3));
}

export async function probeVideoFile(filePath: string): Promise<ProbeVideoMetadata> {
  let stdout: string;
  try {
    ({ stdout } = await runProcess("ffprobe", [
      "-v",
      "error",
      "-show_entries",
      "format=duration,size,bit_rate,format_name:stream=index,codec_type,codec_name,width,height,avg_frame_rate,sample_rate,channels",
      "-of",
      "json",
      filePath,
    ]));
  } catch (err) {
    if (isCorruptMediaError(err)) {
      throw new CorruptMediaError(filePath, err instanceof Error ? err : undefined);
    }
    throw err;
  }

  const parsed = JSON.parse(stdout) as FfprobeResult;
  const videoStream = parsed.streams?.find((stream) => stream.codec_type === "video");
  const audioStream = parsed.streams?.find((stream) => stream.codec_type === "audio");
  const formatName = parsed.format?.format_name?.split(",")[0] ?? null;
  const extContainer = path.extname(filePath).replace(".", "");
  const container = formatName ?? (extContainer || null);

  return {
    filePath,
    fileName: path.basename(filePath),
    duration: parsed.format?.duration ? Number(parsed.format.duration) : null,
    fileSize: parsed.format?.size ? Number(parsed.format.size) : null,
    bitRate: parsed.format?.bit_rate ? Number(parsed.format.bit_rate) : null,
    width: videoStream?.width ?? null,
    height: videoStream?.height ?? null,
    frameRate: parseFrameRate(videoStream?.avg_frame_rate),
    codec: videoStream?.codec_name ?? null,
    container,
    audio: audioStream
      ? {
          codec: audioStream.codec_name ?? null,
          sampleRate: audioStream.sample_rate ? Number(audioStream.sample_rate) : null,
          channels: audioStream.channels ?? null,
        }
      : null,
  };
}

export async function discoverVideoFiles(rootPath: string, recursive = true): Promise<string[]> {
  const entries = await readdir(rootPath, { withFileTypes: true });
  const files: string[] = [];

  for (const entry of entries) {
    // Skip hidden files/directories (unix-style dot-prefixed: .thumbs, .git, etc.)
    if (entry.name.startsWith(".")) continue;

    const entryPath = path.join(rootPath, entry.name);

    if (entry.isDirectory()) {
      if (recursive) {
        files.push(...(await discoverVideoFiles(entryPath, recursive)));
      }
      continue;
    }

    if (!entry.isFile() || !isVideoFile(entryPath)) {
      continue;
    }

    files.push(entryPath);
  }

  return files.sort((left, right) => left.localeCompare(right));
}

/**
 * Read buffer size for full-file hash streaming. The Node default of 64 KB
 * triggers a syscall (and a JS-side data event) for every chunk, which is
 * meaningful overhead for multi-GB files. 4 MB cuts that ~64x with no
 * downside on modern memory.
 */
const HASH_READ_BUFFER_BYTES = 4 * 1024 * 1024;

/** Stash-compatible "OpenSubtitles hash": 64 KB head + 64 KB tail + filesize. */
const OSHASH_CHUNK_BYTES = 64 * 1024;

export async function computeMd5(filePath: string) {
  const hash = createHash("md5");

  await new Promise<void>((resolve, reject) => {
    const stream = createReadStream(filePath, {
      highWaterMark: HASH_READ_BUFFER_BYTES,
    });
    stream.on("data", (chunk) => hash.update(chunk));
    stream.on("error", reject);
    stream.on("end", () => resolve());
  });

  return hash.digest("hex");
}

/**
 * Compute MD5 + OpenSubtitles hash in a single read pass.
 *
 * The previous fingerprint pipeline ran `computeMd5(file)` and then
 * `computeOsHash(file)` sequentially, which on a cold cache opens the file
 * twice and re-reads the head/tail under a separate handle. This helper
 * streams the file once, snapshots the first 64 KB inline as md5 advances,
 * then reads the trailing 64 KB at the end (which is typically already in
 * the OS page cache from the streaming read). Output is bit-identical to
 * calling `computeMd5` and `computeOsHash` separately.
 *
 * On warm cache the wall-clock saving is modest (~10–15%); on cold cache
 * it avoids a redundant disk seek to the head and removes any chance of
 * parallel-seek thrashing if a caller had instead tried `Promise.all`.
 */
export async function computeMd5AndOsHash(
  filePath: string,
): Promise<{ md5: string; oshash: string }> {
  const stats = await stat(filePath);
  const md5 = createHash("md5");
  let head: Buffer | null = null;
  let headRemaining = OSHASH_CHUNK_BYTES;

  await new Promise<void>((resolve, reject) => {
    const stream = createReadStream(filePath, {
      highWaterMark: HASH_READ_BUFFER_BYTES,
    });
    stream.on("data", (chunk: string | Buffer) => {
      const buf = typeof chunk === "string" ? Buffer.from(chunk) : chunk;
      md5.update(buf);
      if (headRemaining > 0) {
        if (head === null) head = Buffer.alloc(OSHASH_CHUNK_BYTES);
        const take = Math.min(headRemaining, buf.length);
        buf.copy(head, OSHASH_CHUNK_BYTES - headRemaining, 0, take);
        headRemaining -= take;
      }
    });
    stream.on("error", reject);
    stream.on("end", () => resolve());
  });

  // For sources smaller than the 64 KB chunk, the unfilled tail of `head`
  // is left zeroed — matching what computeOsHash would produce on the same
  // file (its read() short-reads and the rest of the buffer stays at zero).
  if (head === null) head = Buffer.alloc(OSHASH_CHUNK_BYTES);

  const tail = Buffer.alloc(OSHASH_CHUNK_BYTES);
  const handle = await open(filePath, "r");
  try {
    await handle.read(
      tail,
      0,
      OSHASH_CHUNK_BYTES,
      Math.max(0, stats.size - OSHASH_CHUNK_BYTES),
    );
  } finally {
    await handle.close();
  }

  let h = BigInt(stats.size);
  for (let i = 0; i < OSHASH_CHUNK_BYTES; i += 8) {
    h += readUInt64LE(head, i);
    h += readUInt64LE(tail, i);
  }

  return {
    md5: md5.digest("hex"),
    oshash: (h & BigInt("0xFFFFFFFFFFFFFFFF"))
      .toString(16)
      .padStart(16, "0"),
  };
}

/**
 * Compute a Stash-compatible video perceptual hash by shelling out to the
 * bundled `prismedia-phash` Go helper.
 *
 * - Returns `null` when duration is unknown or <= 0 (phash is undefined for
 *   zero-length sources and Stash skips submission in that case).
 * - Returns `null` and logs a warning when the binary is not installed. This
 *   lets dev machines without the helper still run the rest of the fingerprint
 *   pipeline; production Docker images always bundle the binary.
 * - Throws on any other non-zero exit so real failures surface in job_runs.
 */
export async function computePhash(
  filePath: string,
  duration: number | null | undefined,
): Promise<string | null> {
  if (!duration || duration <= 0) return null;

  const bin = process.env.PRISMEDIA_PHASH_BIN ?? "prismedia-phash";

  try {
    const { stdout } = await runProcess(bin, [
      "-file",
      filePath,
      "-duration",
      String(duration),
    ]);
    const hash = stdout.trim();
    if (!/^[0-9a-f]{16}$/.test(hash)) {
      throw new Error(`prismedia-phash returned unexpected output: ${hash}`);
    }
    return hash;
  } catch (err: unknown) {
    const code = (err as NodeJS.ErrnoException)?.code;
    if (code === "ENOENT") {
      console.warn(
        `[computePhash] ${bin} not found on PATH — skipping phash for ${filePath}. ` +
          `Install the helper (see infra/phash) or build the unified Docker image to enable phash generation.`,
      );
      return null;
    }
    throw err;
  }
}

function readUInt64LE(buffer: Buffer, offset: number) {
  return buffer.readBigUInt64LE(offset);
}

export async function computeOsHash(filePath: string) {
  const stats = await stat(filePath);
  const chunkSize = 64 * 1024;
  const handle = await open(filePath, "r");

  try {
    const head = Buffer.alloc(chunkSize);
    const tail = Buffer.alloc(chunkSize);

    await handle.read(head, 0, chunkSize, 0);
    await handle.read(tail, 0, chunkSize, Math.max(0, stats.size - chunkSize));

    let hash = BigInt(stats.size);

    for (let index = 0; index < chunkSize; index += 8) {
      hash += readUInt64LE(head, index);
      hash += readUInt64LE(tail, index);
    }

    return (hash & BigInt("0xFFFFFFFFFFFFFFFF")).toString(16).padStart(16, "0");
  } finally {
    await handle.close();
  }
}

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

/**
 * Cache roots we should search when reading generated assets.
 * The shared workspace cache is canonical, with the .NET dev cache included
 * so local backend-generated media can still be discovered.
 */
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

export * from "./subtitles";

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

/**
 * Get sidecar file paths for a video file.
 * E.g. `/media/video.mp4` → `/media/video-thumb.jpg`
 */
export function getSidecarPaths(videoFilePath: string) {
  const dir = path.dirname(videoFilePath);
  const stem = path.basename(videoFilePath, path.extname(videoFilePath));

  return {
    thumbnail: path.join(dir, `${stem}-thumb.jpg`),
    cardThumbnail: path.join(dir, `${stem}-card.jpg`),
    preview: path.join(dir, `${stem}-preview.mp4`),
    sprite: path.join(dir, `${stem}-sprite.jpg`),
    trickplayVtt: path.join(dir, `${stem}-trickplay.vtt`),
    nfo: path.join(dir, `${stem}.nfo`),
    infoJson: path.join(dir, `${stem}.info.json`),
  };
}

/** Filenames under `getGeneratedVideoDir(videoId)` for dedicated video derivatives. */
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

/**
 * Absolute disk paths for generated video assets (thumb, card, preview, sprite, trickplay VTT).
 * Dedicated layout uses `PRISMEDIA_CACHE_DIR/videos/<videoId>/`; sidecar uses names next to the video file.
 */
export function getVideoGeneratedDiskPaths(
  videoId: string,
  videoFilePath: string,
  layout: VideoGeneratedLayout
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

/** Every absolute path where video derivatives may exist across both layouts. */
export function allVideoGeneratedDiskPaths(videoId: string, videoFilePath: string): string[] {
  const dedicated = getVideoGeneratedDiskPaths(videoId, videoFilePath, "dedicated");
  const sidecar = getVideoGeneratedDiskPaths(videoId, videoFilePath, "sidecar");
  return [...new Set([...Object.values(dedicated), ...Object.values(sidecar)])];
}

// ─── NFO metadata ──────────────────────────────────────────────────

export interface NfoMetadata {
  title?: string;
  plot?: string;
  aired?: string;
  studio?: string;
  rating?: number;
  genres?: string[];
  tags?: string[];
  runtime?: number;
  duration?: string;
  url?: string;
}

/**
 * Unified sidecar metadata — superset of NfoMetadata with additional fields
 * that JSON sidecars (yt-dlp .info.json, etc.) can provide.
 */
export interface SidecarMetadata {
  title?: string;
  details?: string;
  date?: string;
  studio?: string;
  rating?: number;
  url?: string;
  urls?: string[];
  tags?: string[];
  performers?: string[];
  duration?: number;
}

function xmlEscape(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&apos;");
}

/** Decode standard XML entities that appear in NFO sidecar files. */
function decodeXmlEntities(value: string): string {
  return value
    .replace(/&apos;/gi, "'")
    .replace(/&quot;/gi, '"')
    .replace(/&lt;/gi, "<")
    .replace(/&gt;/gi, ">")
    .replace(/&#x([0-9a-f]+);/gi, (_, hex) => String.fromCharCode(parseInt(hex, 16)))
    .replace(/&#(\d+);/g, (_, dec) => String.fromCharCode(Number(dec)))
    .replace(/&amp;/gi, "&"); // must be last
}

function extractTag(xml: string, tag: string): string | null {
  const regex = new RegExp(`<${tag}>([\\s\\S]*?)</${tag}>`);
  const match = xml.match(regex);
  return match ? decodeXmlEntities(match[1].trim()) : null;
}

function extractAllTags(xml: string, tag: string): string[] {
  const regex = new RegExp(`<${tag}>([\\s\\S]*?)</${tag}>`, "g");
  const results: string[] = [];
  let match: RegExpExecArray | null;
  while ((match = regex.exec(xml)) !== null) {
    results.push(match[1].trim());
  }
  return results;
}

const naturalPathCollator = new Intl.Collator(undefined, {
  numeric: true,
  sensitivity: "base",
});

export function naturalComparePaths(a: string, b: string): number {
  return naturalPathCollator.compare(a, b);
}

export function sortPathsNaturally<T extends string>(paths: T[]): T[] {
  return [...paths].sort(naturalComparePaths);
}

export interface ComicInfoMetadata {
  title?: string;
  series?: string;
  number?: string;
  count?: number;
  volume?: number;
  summary?: string;
  date?: string;
  publisher?: string;
  urls: string[];
  pageCount?: number;
  language?: string;
  format?: string;
  manga?: string;
  ageRating?: string;
  creators: string[];
  tags: string[];
}

function cleanComicInfoValue(value: string | null): string | undefined {
  const trimmed = value?.replace(/^\uFEFF/, "").trim();
  if (!trimmed || trimmed === "-1") return undefined;
  return trimmed;
}

function comicInfoNumber(xml: string, tag: string): number | undefined {
  const raw = cleanComicInfoValue(extractTag(xml, tag));
  if (!raw) return undefined;
  const value = Number(raw);
  return Number.isFinite(value) && value >= 0 ? value : undefined;
}

function splitComicInfoList(value: string | undefined): string[] {
  if (!value) return [];
  return value
    .split(/[;,]/)
    .map((part) => part.trim())
    .filter(Boolean);
}

function uniqueStrings(values: string[]): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const value of values) {
    const key = value.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(value);
  }
  return out;
}

function comicInfoDate(xml: string): string | undefined {
  const year = comicInfoNumber(xml, "Year");
  if (!year || year < 1) return undefined;
  const month = comicInfoNumber(xml, "Month");
  const day = comicInfoNumber(xml, "Day");
  if (!month || month < 1 || month > 12) return String(year);
  if (!day || day < 1 || day > 31) {
    return `${year}-${String(month).padStart(2, "0")}`;
  }
  return `${year}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
}

export function parseComicInfoXml(xml: string): ComicInfoMetadata {
  const publisher =
    cleanComicInfoValue(extractTag(xml, "Publisher")) ??
    cleanComicInfoValue(extractTag(xml, "Imprint"));

  const creatorTags = [
    "Writer",
    "Penciller",
    "Inker",
    "Colorist",
    "Letterer",
    "CoverArtist",
    "Editor",
    "Translator",
  ];
  const creators = uniqueStrings(
    creatorTags.flatMap((tag) =>
      splitComicInfoList(cleanComicInfoValue(extractTag(xml, tag))),
    ),
  );

  const tags = uniqueStrings([
    ...splitComicInfoList(cleanComicInfoValue(extractTag(xml, "Genre"))),
    ...splitComicInfoList(cleanComicInfoValue(extractTag(xml, "Tags"))),
    ...splitComicInfoList(cleanComicInfoValue(extractTag(xml, "Characters"))),
    ...splitComicInfoList(cleanComicInfoValue(extractTag(xml, "SeriesGroup"))),
    ...splitComicInfoList(cleanComicInfoValue(extractTag(xml, "StoryArc"))),
    ...splitComicInfoList(cleanComicInfoValue(extractTag(xml, "Manga"))),
    ...splitComicInfoList(cleanComicInfoValue(extractTag(xml, "AgeRating"))),
  ]);

  const urls = uniqueStrings(
    splitComicInfoList(cleanComicInfoValue(extractTag(xml, "Web"))),
  );

  const metadata: ComicInfoMetadata = {
    title: cleanComicInfoValue(extractTag(xml, "Title")),
    series: cleanComicInfoValue(extractTag(xml, "Series")),
    number: cleanComicInfoValue(extractTag(xml, "Number")),
    count: comicInfoNumber(xml, "Count"),
    volume: comicInfoNumber(xml, "Volume"),
    summary: cleanComicInfoValue(extractTag(xml, "Summary")),
    date: comicInfoDate(xml),
    publisher,
    urls,
    pageCount: comicInfoNumber(xml, "PageCount"),
    language: cleanComicInfoValue(extractTag(xml, "LanguageISO")),
    format: cleanComicInfoValue(extractTag(xml, "Format")),
    manga: cleanComicInfoValue(extractTag(xml, "Manga")),
    ageRating: cleanComicInfoValue(extractTag(xml, "AgeRating")),
    creators,
    tags,
  };

  for (const key of Object.keys(metadata) as Array<keyof ComicInfoMetadata>) {
    if (metadata[key] === undefined) {
      delete metadata[key];
    }
  }

  return metadata;
}

export function extractComicInfoFromZip(zipPath: string): ComicInfoMetadata | null {
  const zip = new AdmZip(zipPath);
  const entry = zip
    .getEntries()
    .find(
      (candidate) =>
        !candidate.isDirectory &&
        path.basename(candidate.entryName).toLowerCase() === "comicinfo.xml",
    );
  if (!entry) return null;
  return parseComicInfoXml(entry.getData().toString("utf8"));
}

/**
 * Normalize a raw NFO rating to the 0-100 scale used by the database.
 * Common NFO scales: 0-5 (stars), 0-10 (decimal), 0-100 (percentage).
 * Values above 100 are treated as vote counts and ignored.
 */
export function normalizeNfoRating(raw: number): number | null {
  if (!Number.isFinite(raw) || raw < 0) return null;
  if (raw > 100) return null; // likely a vote count, not a rating
  if (raw <= 5) return Math.round(raw * 20);
  if (raw <= 10) return Math.round(raw * 10);
  return Math.round(raw);
}

export async function readNfo(videoFilePath: string): Promise<NfoMetadata | null> {
  const nfoPath = getSidecarPaths(videoFilePath).nfo;

  if (!existsSync(nfoPath)) {
    return null;
  }

  try {
    const xml = await readFile(nfoPath, "utf8");

    const runtimeStr = extractTag(xml, "runtime");
    const ratingStr = extractTag(xml, "rating");

    return {
      title: extractTag(xml, "title") ?? undefined,
      plot: extractTag(xml, "plot") ?? undefined,
      aired: extractTag(xml, "aired") ?? undefined,
      studio: extractTag(xml, "studio") ?? undefined,
      rating: ratingStr ? Number(ratingStr) : undefined,
      genres: extractAllTags(xml, "genre"),
      tags: extractAllTags(xml, "tag"),
      runtime: runtimeStr ? Number(runtimeStr) : undefined,
      duration: extractTag(xml, "duration") ?? undefined,
      url: extractTag(xml, "url") ?? undefined,
    };
  } catch {
    return null;
  }
}

/**
 * Read a JSON sidecar file (e.g. yt-dlp .info.json) next to a video file.
 *
 * Looks for `<stem>.info.json` first, then `<stem>.json`.
 * Maps common fields from yt-dlp and other download tools to SidecarMetadata.
 */
export async function readSidecarJson(
  videoFilePath: string,
): Promise<SidecarMetadata | null> {
  const sidecar = getSidecarPaths(videoFilePath);
  const dir = path.dirname(videoFilePath);
  const stem = path.basename(videoFilePath, path.extname(videoFilePath));

  // Try .info.json first (yt-dlp convention), then .json
  const candidates = [sidecar.infoJson, path.join(dir, `${stem}.json`)];

  let raw: string | null = null;
  for (const candidate of candidates) {
    if (existsSync(candidate)) {
      try {
        raw = await readFile(candidate, "utf8");
        break;
      } catch {
        // Skip unreadable files
      }
    }
  }

  if (!raw) return null;

  try {
    const json = JSON.parse(raw);
    if (!json || typeof json !== "object") return null;

    return parseSidecarJson(json);
  } catch {
    return null;
  }
}

/**
 * Parse a generic JSON sidecar into SidecarMetadata.
 * Handles yt-dlp format, as well as generic field names.
 */
function parseSidecarJson(json: Record<string, unknown>): SidecarMetadata {
  const result: SidecarMetadata = {};

  // Title: yt-dlp uses "title" or "fulltitle"
  const title = firstString(json, "title", "fulltitle");
  if (title) result.title = title;

  // Details/description
  const details = firstString(json, "description", "plot", "synopsis");
  if (details) result.details = details;

  // Date: yt-dlp uses "upload_date" (YYYYMMDD format) or "release_date"
  const uploadDate = firstString(json, "upload_date", "release_date");
  if (uploadDate) {
    result.date = normalizeSidecarDate(uploadDate);
  } else {
    const dateStr = firstString(json, "date", "aired");
    if (dateStr) result.date = normalizeSidecarDate(dateStr);
  }

  // Studio/uploader: yt-dlp uses "uploader", "channel", "creator"
  const studio = firstString(json, "uploader", "channel", "creator", "studio", "artist");
  if (studio) result.studio = studio;

  // URL: yt-dlp uses "webpage_url", also check "url" (but skip stream URLs)
  const webpageUrl = firstString(json, "webpage_url", "original_url");
  if (webpageUrl && (webpageUrl.startsWith("http://") || webpageUrl.startsWith("https://"))) {
    result.url = webpageUrl;
    result.urls = [webpageUrl];
  } else {
    const url = firstString(json, "url");
    if (url && (url.startsWith("http://") || url.startsWith("https://")) && !isStreamUrl(url)) {
      result.url = url;
      result.urls = [url];
    }
  }

  // Tags: yt-dlp has "tags" (array of strings), also merge "categories"
  const tags: string[] = [];
  if (Array.isArray(json.tags)) {
    for (const t of json.tags) {
      if (typeof t === "string" && t.trim()) tags.push(t.trim());
    }
  }
  if (Array.isArray(json.categories)) {
    for (const c of json.categories) {
      if (typeof c === "string" && c.trim()) tags.push(c.trim());
    }
  }
  // Also check "genre" (single or array)
  if (typeof json.genre === "string" && json.genre.trim()) {
    tags.push(json.genre.trim());
  } else if (Array.isArray(json.genre)) {
    for (const g of json.genre) {
      if (typeof g === "string" && g.trim()) tags.push(g.trim());
    }
  }
  if (tags.length > 0) {
    // Deduplicate case-insensitively
    const seen = new Set<string>();
    result.tags = tags.filter((t) => {
      const lower = t.toLowerCase();
      if (seen.has(lower)) return false;
      seen.add(lower);
      return true;
    });
  }

  // Performers: yt-dlp doesn't have a standard performer field, but check
  // "performers", "actors", "cast", and "uploader" for multi-person content
  const performers: string[] = [];
  for (const key of ["performers", "actors", "cast"]) {
    const val = json[key];
    if (Array.isArray(val)) {
      for (const p of val) {
        if (typeof p === "string" && p.trim()) performers.push(p.trim());
      }
    } else if (typeof val === "string" && val.trim()) {
      performers.push(val.trim());
    }
  }
  if (performers.length > 0) {
    const seen = new Set<string>();
    result.performers = performers.filter((p) => {
      const lower = p.toLowerCase();
      if (seen.has(lower)) return false;
      seen.add(lower);
      return true;
    });
  }

  // Duration: yt-dlp uses "duration" in seconds
  if (typeof json.duration === "number" && json.duration > 0) {
    result.duration = json.duration;
  }

  // Rating: yt-dlp has "like_count"/"dislike_count"/"average_rating" but not standard 0-100
  if (typeof json.average_rating === "number") {
    result.rating = normalizeNfoRating(json.average_rating) ?? undefined;
  }

  return result;
}

/** Grab the first non-empty string value from a set of keys on an object. */
function firstString(obj: Record<string, unknown>, ...keys: string[]): string | null {
  for (const key of keys) {
    const val = obj[key];
    if (typeof val === "string" && val.trim()) return val.trim();
  }
  return null;
}

/** Detect stream/CDN URLs that aren't useful as source URLs. */
function isStreamUrl(url: string): boolean {
  return (
    url.includes(".m3u8") ||
    url.includes(".mpd") ||
    url.includes("manifest") ||
    url.includes("index-v1") ||
    url.includes("/hls/")
  );
}

/** Normalize date strings: YYYYMMDD → YYYY-MM-DD, or pass through if already formatted. */
function normalizeSidecarDate(raw: string): string {
  // YYYYMMDD format (yt-dlp)
  if (/^\d{8}$/.test(raw)) {
    return `${raw.slice(0, 4)}-${raw.slice(4, 6)}-${raw.slice(6, 8)}`;
  }
  // Already YYYY-MM-DD
  if (/^\d{4}-\d{2}-\d{2}$/.test(raw)) {
    return raw;
  }
  // Try parsing as date
  const parsed = new Date(raw);
  if (!Number.isNaN(parsed.getTime())) {
    const y = parsed.getFullYear();
    const m = String(parsed.getMonth() + 1).padStart(2, "0");
    const d = String(parsed.getDate()).padStart(2, "0");
    return `${y}-${m}-${d}`;
  }
  return raw;
}

/**
 * Read all sidecar metadata for a video file.
 *
 * Checks both NFO (XML) and JSON (.info.json) sidecars.
 * NFO values take priority when both exist; JSON fills gaps.
 * Returns null if no sidecar files are found.
 */
export async function readSidecarMetadata(
  videoFilePath: string,
): Promise<SidecarMetadata | null> {
  const [nfo, json] = await Promise.all([
    readNfo(videoFilePath),
    readSidecarJson(videoFilePath),
  ]);

  if (!nfo && !json) return null;

  // Start with JSON as base (lower priority), overlay NFO on top
  const result: SidecarMetadata = {};

  // JSON base
  if (json) {
    if (json.title) result.title = json.title;
    if (json.details) result.details = json.details;
    if (json.date) result.date = json.date;
    if (json.studio) result.studio = json.studio;
    if (json.rating != null) result.rating = json.rating;
    if (json.url) result.url = json.url;
    if (json.urls?.length) result.urls = json.urls;
    if (json.tags?.length) result.tags = json.tags;
    if (json.performers?.length) result.performers = json.performers;
    if (json.duration) result.duration = json.duration;
  }

  // NFO overlay (takes precedence)
  if (nfo) {
    if (nfo.title) result.title = nfo.title;
    if (nfo.plot) result.details = nfo.plot;
    if (nfo.aired) result.date = nfo.aired;
    if (nfo.studio) result.studio = nfo.studio;
    if (nfo.rating != null) result.rating = normalizeNfoRating(nfo.rating) ?? result.rating;
    if (nfo.url) {
      result.url = nfo.url;
      // Merge NFO url into urls array
      const existing = new Set(result.urls ?? []);
      existing.add(nfo.url);
      result.urls = Array.from(existing);
    }
    // Merge NFO tags/genres into tags
    const nfoTags = [...(nfo.tags ?? []), ...(nfo.genres ?? [])];
    if (nfoTags.length > 0) {
      const seen = new Set((result.tags ?? []).map((t) => t.toLowerCase()));
      const merged = [...(result.tags ?? [])];
      for (const t of nfoTags) {
        const lower = t.toLowerCase();
        if (!seen.has(lower)) {
          seen.add(lower);
          merged.push(t);
        }
      }
      result.tags = merged;
    }
  }

  return Object.keys(result).length > 0 ? result : null;
}

function formatDurationHMS(totalSeconds: number): string {
  const h = Math.floor(totalSeconds / 3600);
  const m = Math.floor((totalSeconds % 3600) / 60);
  const s = Math.floor(totalSeconds % 60);
  return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
}

export interface NfoWriteData {
  title: string;
  plot?: string | null;
  date?: string | null;
  studio?: string | null;
  rating?: number | null;
  genres?: string[];
  tags?: string[];
  duration?: number | null;
  url?: string | null;
}

export async function writeNfo(videoFilePath: string, data: NfoWriteData): Promise<void> {
  const nfoPath = getSidecarPaths(videoFilePath).nfo;

  const lines: string[] = [
    `<?xml version="1.0" encoding="UTF-8"?>`,
    `<episodedetails>`,
    `  <title>${xmlEscape(data.title)}</title>`,
  ];

  const add = (tag: string, value: unknown) => {
    if (value !== undefined && value !== null && value !== "") {
      lines.push(`  <${tag}>${xmlEscape(String(value))}</${tag}>`);
    }
  };

  add("plot", data.plot);
  add("url", data.url);
  add("aired", data.date);
  add("studio", data.studio);

  if (data.rating != null) {
    add("rating", data.rating);
  }

  if (typeof data.duration === "number" && data.duration > 0) {
    add("runtime", Math.round(data.duration / 60));
    add("duration", formatDurationHMS(data.duration));
  }

  if (data.genres) {
    for (const genre of data.genres) {
      add("genre", genre);
    }
  }

  if (data.tags) {
    for (const tag of data.tags) {
      add("tag", tag);
    }
  }

  lines.push(`</episodedetails>`);

  await writeFile(nfoPath, lines.join("\n") + "\n", "utf8");
}

// ─── Image discovery ──────────────────────────────────────────────

export const supportedImageExtensions = new Set([
  ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif", ".heic", ".bmp", ".tiff", ".tif",
]);

/** Video/animated formats that can appear as gallery items (animated images, short clips). */
export const supportedAnimatedExtensions = new Set([
  ".webm", ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".wmv", ".flv",
]);

/** All extensions eligible for gallery discovery (static images + animated). */
export const supportedGalleryMediaExtensions = new Set([
  ...supportedImageExtensions,
  ...supportedAnimatedExtensions,
]);

export const supportedZipExtensions = new Set([
  ".zip", ".cbz", ".cbr",
]);

export function isImageFile(filePath: string): boolean {
  const ext = path.extname(filePath).toLowerCase();
  if (!supportedGalleryMediaExtensions.has(ext)) return false;

  // Skip generated preview/thumbnail/sample files
  const stem = path.basename(filePath, ext);
  return !generatedSuffixPattern.test(stem);
}

/** Check if a gallery media file is an animated/video format rather than a static image. */
export function isAnimatedFormat(filePath: string): boolean {
  const ext = path.extname(filePath).toLowerCase();
  return supportedAnimatedExtensions.has(ext) || ext === ".gif";
}

export interface ImageDiscoveryResult {
  /** Directories containing at least one image file directly */
  dirs: string[];
  /** All image files found (absolute paths) */
  imageFiles: string[];
  /** All zip/cbz/cbr files found (absolute paths) */
  zipFiles: string[];
}

export async function discoverImageFilesAndDirs(
  rootPath: string,
  recursive = true
): Promise<ImageDiscoveryResult> {
  const dirs: Set<string> = new Set();
  const imageFiles: string[] = [];
  const zipFiles: string[] = [];

  async function walk(dirPath: string) {
    const entries = await readdir(dirPath, { withFileTypes: true });

    for (const entry of entries) {
      // Skip hidden files/directories (unix-style dot-prefixed: .thumbs, .git, etc.)
      if (entry.name.startsWith(".")) continue;

      const entryPath = path.join(dirPath, entry.name);

      if (entry.isDirectory()) {
        if (recursive) {
          await walk(entryPath);
        }
        continue;
      }

      if (!entry.isFile()) continue;

      const ext = path.extname(entry.name).toLowerCase();

      if (supportedZipExtensions.has(ext)) {
        zipFiles.push(entryPath);
        continue;
      }

      if (isImageFile(entryPath)) {
        imageFiles.push(entryPath);
        dirs.add(dirPath);
      }
    }
  }

  await walk(rootPath);

  return {
    dirs: [...dirs].sort(),
    imageFiles: imageFiles.sort(),
    zipFiles: zipFiles.sort(),
  };
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

/**
 * Parse a zip/cbz/cbr file and return sorted member paths for image entries.
 */
export function parseZipImageMembers(zipPath: string): string[] {
  const zip = new AdmZip(zipPath);
  const entries = zip.getEntries();

  return entries
    .filter((entry) => {
      if (entry.isDirectory) return false;
      const ext = path.extname(entry.entryName).toLowerCase();
      return supportedImageExtensions.has(ext);
    })
    .map((entry) => entry.entryName)
    .sort(naturalComparePaths);
}

/**
 * Extract a single member from a zip file as a Buffer.
 */
export function extractZipMember(zipPath: string, memberPath: string): Buffer | null {
  const zip = new AdmZip(zipPath);
  const entry = zip.getEntry(memberPath);
  if (!entry) return null;
  return entry.getData();
}

/**
 * Probe an image file to extract width, height, and format using ffprobe.
 */
export async function probeImageFile(filePath: string): Promise<{
  width: number | null;
  height: number | null;
  format: string | null;
}> {
  try {
    const { stdout } = await runProcess("ffprobe", [
      "-v", "error",
      "-select_streams", "v:0",
      "-show_entries", "stream=width,height,codec_name",
      "-of", "json",
      filePath,
    ]);

    const parsed = JSON.parse(stdout) as { streams?: Array<{ width?: number; height?: number; codec_name?: string }> };
    const stream = parsed.streams?.[0];

    return {
      width: stream?.width ?? null,
      height: stream?.height ?? null,
      format: stream?.codec_name ?? null,
    };
  } catch {
    return { width: null, height: null, format: null };
  }
}

// ─── Audio discovery ──────────────────────────────────────────────

export const supportedAudioExtensions = new Set([
  ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".wma", ".opus",
  ".aiff", ".aif", ".alac", ".ape", ".dsf", ".dff", ".wv",
]);

export function isAudioFile(filePath: string): boolean {
  const ext = path.extname(filePath).toLowerCase();
  if (!supportedAudioExtensions.has(ext)) return false;
  const stem = path.basename(filePath, ext);
  return !generatedSuffixPattern.test(stem);
}

export interface AudioDiscoveryResult {
  /** Directories containing at least one audio file directly */
  dirs: string[];
  /** All audio files found (absolute paths) */
  audioFiles: string[];
}

export async function discoverAudioFilesAndDirs(
  rootPath: string,
  recursive = true,
): Promise<AudioDiscoveryResult> {
  const dirs: Set<string> = new Set();
  const audioFiles: string[] = [];

  async function walk(dirPath: string) {
    const entries = await readdir(dirPath, { withFileTypes: true });

    for (const entry of entries) {
      if (entry.name.startsWith(".")) continue;

      const entryPath = path.join(dirPath, entry.name);

      if (entry.isDirectory()) {
        if (recursive) {
          await walk(entryPath);
        }
        continue;
      }

      if (!entry.isFile()) continue;

      if (isAudioFile(entryPath)) {
        audioFiles.push(entryPath);
        dirs.add(dirPath);
      }
    }
  }

  await walk(rootPath);

  return {
    dirs: [...dirs].sort(),
    audioFiles: audioFiles.sort(),
  };
}

// ─── Audio probing ────────────────────────────────────────────────

interface FfprobeFormatTags {
  artist?: string;
  album?: string;
  title?: string;
  track?: string;
  ARTIST?: string;
  ALBUM?: string;
  TITLE?: string;
  TRACK?: string;
}

interface FfprobeAudioResult {
  streams?: FfprobeStream[];
  format?: FfprobeFormat & { tags?: FfprobeFormatTags };
}

export interface ProbeAudioFileMetadata {
  filePath: string;
  fileName: string;
  duration: number | null;
  fileSize: number | null;
  bitRate: number | null;
  sampleRate: number | null;
  channels: number | null;
  codec: string | null;
  container: string | null;
  embeddedArtist: string | null;
  embeddedAlbum: string | null;
  embeddedTitle: string | null;
  trackNumber: number | null;
}

export async function probeAudioFile(filePath: string): Promise<ProbeAudioFileMetadata> {
  const { stdout } = await runProcess("ffprobe", [
    "-v", "error",
    "-show_entries",
    "format=duration,size,bit_rate,format_name:format_tags=artist,album,title,track:stream=codec_name,sample_rate,channels",
    "-of", "json",
    filePath,
  ]);

  const parsed = JSON.parse(stdout) as FfprobeAudioResult;
  const audioStream = parsed.streams?.find((s) => s.codec_name);
  const tags = parsed.format?.tags;
  const formatName = parsed.format?.format_name?.split(",")[0] ?? null;
  const extContainer = path.extname(filePath).replace(".", "").toLowerCase();

  // Tags can be capitalized or lowercase depending on format
  const artist = tags?.artist ?? tags?.ARTIST ?? null;
  const album = tags?.album ?? tags?.ALBUM ?? null;
  const title = tags?.title ?? tags?.TITLE ?? null;
  const trackStr = tags?.track ?? tags?.TRACK ?? null;
  const trackNumber = trackStr ? parseInt(trackStr, 10) : null;

  return {
    filePath,
    fileName: path.basename(filePath),
    duration: parsed.format?.duration ? Number(parsed.format.duration) : null,
    fileSize: parsed.format?.size ? Number(parsed.format.size) : null,
    bitRate: parsed.format?.bit_rate ? Number(parsed.format.bit_rate) : null,
    sampleRate: audioStream?.sample_rate ? Number(audioStream.sample_rate) : null,
    channels: audioStream?.channels ?? null,
    codec: audioStream?.codec_name ?? null,
    container: formatName ?? (extContainer || null),
    embeddedArtist: artist || null,
    embeddedAlbum: album || null,
    embeddedTitle: title || null,
    trackNumber: Number.isFinite(trackNumber) ? trackNumber : null,
  };
}

// ─── Audio waveform generation ────────────────────────────────────

/** Formats natively supported by the audiowaveform binary. */
const audiowaveformNativeFormats = new Set([".mp3", ".wav", ".flac", ".ogg", ".opus"]);

/** Check whether a binary exists on the system PATH. */
async function hasBinary(name: string): Promise<boolean> {
  try {
    await runProcess("which", [name]);
    return true;
  } catch {
    return false;
  }
}

/**
 * ffmpeg-only fallback: decode audio to raw PCM, compute peaks in Node.
 * Produces the same JSON format as audiowaveform: `{ data: number[] }`.
 */
async function generateWaveformWithFfmpeg(
  inputPath: string,
  outputPath: string,
  pixelsPerSecond: number,
): Promise<void> {
  // Get duration first
  const probeResult = await runProcess("ffprobe", [
    "-v", "error",
    "-show_entries", "format=duration",
    "-of", "json",
    inputPath,
  ]);
  const probeParsed = JSON.parse(probeResult.stdout) as { format?: { duration?: string } };
  const duration = Number(probeParsed.format?.duration ?? 0);
  if (duration <= 0) {
    await writeFile(outputPath, JSON.stringify({ data: [] }), "utf8");
    return;
  }

  const totalSamples = Math.ceil(duration * pixelsPerSecond);
  const sampleRate = 8000; // low rate is fine for peaks
  const totalPcmSamples = Math.ceil(duration * sampleRate);
  const samplesPerBucket = Math.max(1, Math.floor(totalPcmSamples / totalSamples));

  // Decode to raw 16-bit signed LE mono PCM
  const pcmResult = await new Promise<Buffer>((resolve, reject) => {
    const ffmpeg = spawn("ffmpeg", [
      "-i", inputPath,
      "-f", "s16le", "-ac", "1", "-ar", String(sampleRate),
      "pipe:1",
    ], { stdio: ["ignore", "pipe", "ignore"] });

    const chunks: Buffer[] = [];
    ffmpeg.stdout.on("data", (chunk: Buffer) => chunks.push(chunk));
    ffmpeg.on("close", (code) => {
      if (code === 0) resolve(Buffer.concat(chunks));
      else reject(new Error(`ffmpeg waveform decode exited with code ${code}`));
    });
    ffmpeg.on("error", reject);
  });

  // Compute min/max peaks per bucket
  const data: number[] = [];
  const sampleCount = Math.floor(pcmResult.length / 2); // 16-bit = 2 bytes per sample

  for (let bucket = 0; bucket < totalSamples; bucket++) {
    const startSample = bucket * samplesPerBucket;
    const endSample = Math.min(startSample + samplesPerBucket, sampleCount);
    let min = 0;
    let max = 0;

    for (let i = startSample; i < endSample; i++) {
      const sample = pcmResult.readInt16LE(i * 2);
      if (sample < min) min = sample;
      if (sample > max) max = sample;
    }

    data.push(min, max);
  }

  await writeFile(outputPath, JSON.stringify({ data }), "utf8");
}

/**
 * Generate waveform peaks JSON for audio playback visualization.
 *
 * Prefers BBC audiowaveform if installed; falls back to pure ffmpeg + Node
 * PCM peak computation when the binary is not available (e.g. local dev).
 *
 * Output: JSON file with `{ data: number[] }` — array of min/max amplitude pairs.
 */
export async function generateAudioWaveform(
  inputPath: string,
  outputPath: string,
  pixelsPerSecond = 20,
): Promise<void> {
  const useAudiowaveform = await hasBinary("audiowaveform");

  if (!useAudiowaveform) {
    // Fallback: ffmpeg + Node PCM peak computation
    await generateWaveformWithFfmpeg(inputPath, outputPath, pixelsPerSecond);
    return;
  }

  const ext = path.extname(inputPath).toLowerCase();

  if (audiowaveformNativeFormats.has(ext)) {
    await runProcess("audiowaveform", [
      "-i", inputPath,
      "--pixels-per-second", String(pixelsPerSecond),
      "-b", "8",
      "-o", outputPath,
    ]);
  } else {
    // Pipe through ffmpeg for unsupported formats
    await new Promise<void>((resolve, reject) => {
      const ffmpeg = spawn("ffmpeg", [
        "-i", inputPath, "-f", "wav", "-ac", "1", "-ar", "16000", "pipe:1",
      ], { stdio: ["ignore", "pipe", "ignore"] });

      const aw = spawn("audiowaveform", [
        "--input-format", "wav",
        "--pixels-per-second", String(pixelsPerSecond),
        "-b", "8",
        "-o", outputPath,
      ], { stdio: ["pipe", "ignore", "pipe"] });

      ffmpeg.stdout.pipe(aw.stdin);

      let awStderr = "";
      aw.stderr.on("data", (chunk) => { awStderr += chunk.toString(); });

      aw.on("close", (code) => {
        if (code === 0) resolve();
        else reject(new Error(`audiowaveform exited with code ${code}: ${awStderr}`));
      });

      ffmpeg.on("error", reject);
      aw.on("error", reject);
    });
  }
}

// ─── Audio cache directories ──────────────────────────────────────

export function getGeneratedAudioLibraryDir(libraryId: string) {
  return path.join(getCacheRootDir(), "audio-libraries", libraryId);
}

export function getGeneratedAudioTrackDir(trackId: string) {
  return path.join(getCacheRootDir(), "audio-tracks", trackId);
}

export * from "./parsing";
export * from "./classifier";
