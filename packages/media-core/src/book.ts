import path from "node:path";
import AdmZip from "adm-zip";
import { naturalComparePaths } from "./sorting";
import { supportedImageExtensions } from "./file-classification";

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
  const base =
    numeric != null ? `Volume ${String(numeric).padStart(2, "0")}` : `Volume ${key}`;
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
    creatorTags.flatMap((tag) => splitComicInfoList(cleanComicInfoValue(extractTag(xml, tag)))),
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

  const urls = uniqueStrings(splitComicInfoList(cleanComicInfoValue(extractTag(xml, "Web"))));

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

export function parseZipImageMembers(zipPath: string): string[] {
  const zip = new AdmZip(zipPath);
  return zip
    .getEntries()
    .filter((entry) => !entry.isDirectory && supportedImageExtensions.has(path.extname(entry.entryName).toLowerCase()))
    .map((entry) => entry.entryName)
    .sort(naturalComparePaths);
}

export function extractZipMember(zipPath: string, memberPath: string): Buffer | null {
  const zip = new AdmZip(zipPath);
  const entry = zip.getEntry(memberPath);
  if (!entry) return null;
  return entry.getData();
}
