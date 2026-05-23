import { readdir, readFile } from "node:fs/promises";
import path from "node:path";

export const supportedSubtitleExtensions = new Set([
  ".vtt",
  ".srt",
  ".ass",
  ".ssa",
]);

export type SubtitleFormat = "vtt" | "srt" | "ass" | "ssa";

export interface SidecarSubtitle {
  path: string;
  format: SubtitleFormat;
  language: string;
  label: string | null;
}

export interface ParsedCue {
  start: number;
  end: number;
  text: string;
}

export function isSubtitleFile(filePath: string): boolean {
  return supportedSubtitleExtensions.has(path.extname(filePath).toLowerCase());
}

export function getSubtitleFormat(filePath: string): SubtitleFormat | null {
  const ext = path.extname(filePath).toLowerCase();
  if (ext === ".vtt") return "vtt";
  if (ext === ".srt") return "srt";
  if (ext === ".ass") return "ass";
  if (ext === ".ssa") return "ssa";
  return null;
}

// Common BCP-47 / ISO 639-1 / 639-2 codes we'll treat as language tags when
// they appear as the inner segment of {basename}.{lang}.{ext}. Rather than a
// full BCP-47 parser we whitelist the 2-3 letter lowercase shapes with optional
// region suffix (e.g. "en", "en-us", "por", "zh-hant").
const LANGUAGE_TAG = /^[a-z]{2,3}(?:-[a-z0-9]{2,8})?$/i;

/**
 * Given a video file path, discover sidecar subtitle files in the same
 * directory that share the video's basename.
 *
 * Matches:
 *   movie.mkv     <-  movie.srt           (lang: und)
 *   movie.mkv     <-  movie.en.srt        (lang: en)
 *   movie.mkv     <-  movie.en-US.vtt     (lang: en-US)
 *   movie.mkv     <-  movie.en.sdh.srt    (lang: en, label: sdh)
 */
export async function discoverSubtitleSidecars(
  videoPath: string
): Promise<SidecarSubtitle[]> {
  const dir = path.dirname(videoPath);
  const videoBase = path.basename(videoPath, path.extname(videoPath));

  let entries: string[] = [];
  try {
    entries = await readdir(dir);
  } catch {
    return [];
  }

  const matches: SidecarSubtitle[] = [];

  for (const entry of entries) {
    if (!isSubtitleFile(entry)) continue;
    const entryExt = path.extname(entry).toLowerCase();
    const entryBase = entry.slice(0, -entryExt.length);

    if (entryBase === videoBase) {
      matches.push({
        path: path.join(dir, entry),
        format: getSubtitleFormat(entry)!,
        language: "und",
        label: null,
      });
      continue;
    }

    if (!entryBase.startsWith(videoBase + ".")) continue;
    const suffix = entryBase.slice(videoBase.length + 1);
    if (!suffix) continue;

    const parts = suffix.split(".");
    // First part looks like a language tag
    if (LANGUAGE_TAG.test(parts[0]!)) {
      matches.push({
        path: path.join(dir, entry),
        format: getSubtitleFormat(entry)!,
        language: parts[0]!,
        label: parts.length > 1 ? parts.slice(1).join(".") : null,
      });
    } else {
      // Didn't parse as language tag — treat whole suffix as label.
      matches.push({
        path: path.join(dir, entry),
        format: getSubtitleFormat(entry)!,
        language: "und",
        label: suffix,
      });
    }
  }

  return matches;
}

/**
 * Convert SRT to WebVTT. Handles the common shape:
 *   1
 *   00:00:01,000 --> 00:00:04,000
 *   Hello world
 *
 * - Strip UTF-8 BOM
 * - Prepend WEBVTT header
 * - Replace `,` with `.` in timestamps
 * - Drop the numeric cue-id line that precedes each cue
 */
export function srtToVtt(srt: string): string {
  const cleaned = srt.replace(/^\uFEFF/, "").replace(/\r\n/g, "\n").trim();
  if (!cleaned) return "WEBVTT\n\n";

  const blocks = cleaned.split(/\n{2,}/);
  const out: string[] = ["WEBVTT", ""];

  for (const block of blocks) {
    const lines = block.split("\n");
    // Drop leading numeric cue-id.
    if (lines.length > 0 && /^\d+$/.test(lines[0]!.trim())) {
      lines.shift();
    }
    if (lines.length === 0) continue;

    // Timestamp line: replace commas with dots.
    lines[0] = lines[0]!.replace(
      /(\d\d:\d\d:\d\d),(\d\d\d)\s*-->\s*(\d\d:\d\d:\d\d),(\d\d\d)/,
      "$1.$2 --> $3.$4"
    );

    out.push(lines.join("\n"));
    out.push("");
  }

  return out.join("\n");
}

/**
 * Minimal ASS/SSA → VTT converter. Parses the `Events` section and pulls
 * `Dialogue:` lines, strips override tags, and emits a basic VTT file. This
 * covers the common subtitle case; complex styled ASS (karaoke, animations)
 * will lose formatting but text is preserved.
 */
export function assToVtt(ass: string): string {
  const cleaned = ass.replace(/^\uFEFF/, "").replace(/\r\n/g, "\n");
  const lines = cleaned.split("\n");

  let inEvents = false;
  let format: string[] = [];
  const out: string[] = ["WEBVTT", ""];

  for (const rawLine of lines) {
    const line = rawLine.trim();
    if (!line) continue;

    if (line.toLowerCase().startsWith("[events]")) {
      inEvents = true;
      continue;
    }
    if (line.startsWith("[")) {
      inEvents = false;
      continue;
    }
    if (!inEvents) continue;

    if (line.toLowerCase().startsWith("format:")) {
      format = line
        .slice("format:".length)
        .split(",")
        .map((s) => s.trim().toLowerCase());
      continue;
    }

    if (!line.toLowerCase().startsWith("dialogue:")) continue;

    const rest = line.slice("dialogue:".length).trim();
    // Split into (format.length - 1) commas, last field is the free-form text.
    const parts: string[] = [];
    let remaining = rest;
    for (let i = 0; i < format.length - 1; i++) {
      const commaIdx = remaining.indexOf(",");
      if (commaIdx === -1) break;
      parts.push(remaining.slice(0, commaIdx).trim());
      remaining = remaining.slice(commaIdx + 1);
    }
    parts.push(remaining);

    const get = (key: string): string | undefined => {
      const idx = format.indexOf(key);
      return idx >= 0 ? parts[idx] : undefined;
    };

    const start = get("start");
    const end = get("end");
    const text = get("text");
    if (!start || !end || text === undefined) continue;

    const cueText = text
      .replace(/\{[^}]*\}/g, "") // strip override tags
      .replace(/\\N/gi, "\n") // hard newlines
      .replace(/\\h/gi, " "); // hard spaces

    out.push(`${assTimeToVtt(start)} --> ${assTimeToVtt(end)}`);
    out.push(cueText);
    out.push("");
  }

  return out.join("\n");
}

function assTimeToVtt(time: string): string {
  // ASS: H:MM:SS.cs (centiseconds)
  const match = time.match(/^(\d+):(\d{2}):(\d{2})\.(\d{2})$/);
  if (!match) return "00:00:00.000";
  const [, h, m, s, cs] = match;
  return `${h!.padStart(2, "0")}:${m}:${s}.${cs}0`;
}

/**
 * Convert any supported subtitle file content into WebVTT.
 */
export function normalizeSubtitleToVtt(
  content: string,
  format: SubtitleFormat
): string {
  switch (format) {
    case "vtt":
      return content.replace(/^\uFEFF/, "").startsWith("WEBVTT")
        ? content.replace(/^\uFEFF/, "")
        : "WEBVTT\n\n" + content.replace(/^\uFEFF/, "");
    case "srt":
      return srtToVtt(content);
    case "ass":
    case "ssa":
      return assToVtt(content);
  }
}

export async function readAndNormalizeSubtitle(
  filePath: string
): Promise<string> {
  const format = getSubtitleFormat(filePath);
  if (!format) throw new Error(`Unsupported subtitle file: ${filePath}`);
  const content = await readFile(filePath, "utf8");
  return normalizeSubtitleToVtt(content, format);
}

/**
 * Parse a WebVTT file into structured cues. Intentionally tiny — handles the
 * common cue shape with optional identifier line. Ignores NOTE blocks, STYLE,
 * and positioning settings.
 */
export function parseVttCues(vtt: string): ParsedCue[] {
  const cleaned = vtt.replace(/^\uFEFF/, "").replace(/\r\n/g, "\n");
  const blocks = cleaned.split(/\n{2,}/);
  const cues: ParsedCue[] = [];

  for (const block of blocks) {
    const lines = block.split("\n").filter((l) => l.trim().length > 0);
    if (lines.length === 0) continue;

    // Skip header, NOTE, STYLE blocks.
    if (/^WEBVTT/.test(lines[0]!)) continue;
    if (/^NOTE(\s|$)/.test(lines[0]!)) continue;
    if (/^STYLE(\s|$)/.test(lines[0]!)) continue;

    // Optional identifier line (doesn't contain -->) followed by timing.
    let idx = 0;
    if (!lines[idx]!.includes("-->")) idx++;
    if (idx >= lines.length) continue;

    const timing = lines[idx]!;
    const match = timing.match(
      /^(\d{2,}:\d{2}:\d{2}\.\d{3}|\d{2}:\d{2}\.\d{3})\s*-->\s*(\d{2,}:\d{2}:\d{2}\.\d{3}|\d{2}:\d{2}\.\d{3})/
    );
    if (!match) continue;

    const start = parseVttTimestamp(match[1]!);
    const end = parseVttTimestamp(match[2]!);
    const text = lines
      .slice(idx + 1)
      .join("\n")
      .replace(/<[^>]+>/g, "") // strip inline tags
      .trim();

    if (!text) continue;
    cues.push({ start, end, text });
  }

  return cues;
}

function parseVttTimestamp(ts: string): number {
  const parts = ts.split(":");
  if (parts.length === 3) {
    const [h, m, rest] = parts;
    const [s, ms] = rest!.split(".");
    return (
      Number(h) * 3600 +
      Number(m) * 60 +
      Number(s) +
      Number(ms) / 1000
    );
  }
  if (parts.length === 2) {
    const [m, rest] = parts;
    const [s, ms] = rest!.split(".");
    return Number(m) * 60 + Number(s) + Number(ms) / 1000;
  }
  return 0;
}
