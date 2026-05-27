import { existsSync } from "node:fs";
import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { getSidecarPaths } from "./generated-paths";

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

function decodeXmlEntities(value: string): string {
  return value
    .replace(/&apos;/gi, "'")
    .replace(/&quot;/gi, '"')
    .replace(/&lt;/gi, "<")
    .replace(/&gt;/gi, ">")
    .replace(/&#x([0-9a-f]+);/gi, (_, hex) => String.fromCharCode(parseInt(hex, 16)))
    .replace(/&#(\d+);/g, (_, dec) => String.fromCharCode(Number(dec)))
    .replace(/&amp;/gi, "&");
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

export function normalizeNfoRating(raw: number): number | null {
  if (!Number.isFinite(raw) || raw < 0) return null;
  if (raw > 100) return null;
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

export async function readSidecarJson(
  videoFilePath: string,
): Promise<SidecarMetadata | null> {
  const sidecar = getSidecarPaths(videoFilePath);
  const dir = path.dirname(videoFilePath);
  const stem = path.basename(videoFilePath, path.extname(videoFilePath));
  const candidates = [sidecar.infoJson, path.join(dir, `${stem}.json`)];

  let raw: string | null = null;
  for (const candidate of candidates) {
    if (existsSync(candidate)) {
      try {
        raw = await readFile(candidate, "utf8");
        break;
      } catch {
        // Skip unreadable files.
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

function parseSidecarJson(json: Record<string, unknown>): SidecarMetadata {
  const result: SidecarMetadata = {};

  const title = firstString(json, "title", "fulltitle");
  if (title) result.title = title;

  const details = firstString(json, "description", "plot", "synopsis");
  if (details) result.details = details;

  const uploadDate = firstString(json, "upload_date", "release_date");
  if (uploadDate) {
    result.date = normalizeSidecarDate(uploadDate);
  } else {
    const dateStr = firstString(json, "date", "aired");
    if (dateStr) result.date = normalizeSidecarDate(dateStr);
  }

  const studio = firstString(json, "uploader", "channel", "creator", "studio", "artist");
  if (studio) result.studio = studio;

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
  if (typeof json.genre === "string" && json.genre.trim()) {
    tags.push(json.genre.trim());
  } else if (Array.isArray(json.genre)) {
    for (const g of json.genre) {
      if (typeof g === "string" && g.trim()) tags.push(g.trim());
    }
  }
  if (tags.length > 0) {
    const seen = new Set<string>();
    result.tags = tags.filter((t) => {
      const lower = t.toLowerCase();
      if (seen.has(lower)) return false;
      seen.add(lower);
      return true;
    });
  }

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

  if (typeof json.duration === "number" && json.duration > 0) {
    result.duration = json.duration;
  }

  if (typeof json.average_rating === "number") {
    result.rating = normalizeNfoRating(json.average_rating) ?? undefined;
  }

  return result;
}

function firstString(obj: Record<string, unknown>, ...keys: string[]): string | null {
  for (const key of keys) {
    const val = obj[key];
    if (typeof val === "string" && val.trim()) return val.trim();
  }
  return null;
}

function isStreamUrl(url: string): boolean {
  return (
    url.includes(".m3u8") ||
    url.includes(".mpd") ||
    url.includes("manifest") ||
    url.includes("index-v1") ||
    url.includes("/hls/")
  );
}

function normalizeSidecarDate(raw: string): string {
  if (/^\d{8}$/.test(raw)) {
    return `${raw.slice(0, 4)}-${raw.slice(4, 6)}-${raw.slice(6, 8)}`;
  }
  if (/^\d{4}-\d{2}-\d{2}$/.test(raw)) {
    return raw;
  }
  const parsed = new Date(raw);
  if (!Number.isNaN(parsed.getTime())) {
    const y = parsed.getFullYear();
    const m = String(parsed.getMonth() + 1).padStart(2, "0");
    const d = String(parsed.getDate()).padStart(2, "0");
    return `${y}-${m}-${d}`;
  }
  return raw;
}

export async function readSidecarMetadata(
  videoFilePath: string,
): Promise<SidecarMetadata | null> {
  const [nfo, json] = await Promise.all([
    readNfo(videoFilePath),
    readSidecarJson(videoFilePath),
  ]);

  if (!nfo && !json) return null;

  const result: SidecarMetadata = {};

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

  if (nfo) {
    if (nfo.title) result.title = nfo.title;
    if (nfo.plot) result.details = nfo.plot;
    if (nfo.aired) result.date = nfo.aired;
    if (nfo.studio) result.studio = nfo.studio;
    if (nfo.rating != null) result.rating = normalizeNfoRating(nfo.rating) ?? result.rating;
    if (nfo.url) {
      result.url = nfo.url;
      const existing = new Set(result.urls ?? []);
      existing.add(nfo.url);
      result.urls = Array.from(existing);
    }
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
