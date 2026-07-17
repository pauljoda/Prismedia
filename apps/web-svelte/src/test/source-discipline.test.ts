// Source-discipline guards for the identifier contract (CLAUDE.md) — the frontend
// counterpart of the backend's ConstantsDriftGuardTests / InfrastructureBoundaryTests.
//
// 1. No-magic-codes: distinctive closed-set code values (hyphenated/underscored codes,
//    provider ids, playback modes) must be referenced from the generated `codes.ts`
//    families, never retyped as quoted literals. Ambiguous single-word codes such as
//    "video" or "tags" collide with ordinary UI vocabulary and are enforced in review
//    instead, mirroring the backend guard's unambiguous-literals-only policy.
// 2. Max-file-size: source files stay under the modularity ceiling; current god files
//    carry shrink-only grandfather ceilings that must ratchet down as they are split.
//
// Both allowlists are ratchets: entries may only shrink or disappear. A stale entry
// (no longer matching any hit) fails the guard so the lists cannot rot.
import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import {
  CAPABILITY_KIND,
  ENTITY_KIND,
  EXTERNAL_ID_PROVIDER,
  JOB_TYPE,
  PLAYBACK_MODE,
  PROBLEM_CODE,
  RELATIONSHIP_CODE,
  SUBTITLE_SOURCE,
} from "$lib/api/generated/codes";

const SRC_ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");

/** Families guarded when their values are distinctive (contain `-` or `_`). */
const GUARDED_FAMILIES: Record<string, Record<string, string>> = {
  ENTITY_KIND,
  RELATIONSHIP_CODE,
  JOB_TYPE,
  PLAYBACK_MODE,
  SUBTITLE_SOURCE,
  CAPABILITY_KIND,
  EXTERNAL_ID_PROVIDER,
  PROBLEM_CODE,
};

/** Families guarded for ALL values — their vocabularies never collide with UI text. */
const ALWAYS_GUARDED_FAMILIES = new Set(["EXTERNAL_ID_PROVIDER", "PLAYBACK_MODE"]);

/**
 * Known offenders at guard introduction (2026-07 audit), file → retyped values.
 * Shrink-only: fix a file by importing from `$lib/api/generated/codes` (or
 * `$lib/entities/entity-codes`) and delete its entry. Never add entries for new code.
 */
const MAGIC_CODE_ALLOWLIST: Record<string, string[]> = {
  "lib/api/settings.ts": ["direct", "hls"],
  "lib/components/AudioTrackList.svelte": ["audio-track"],
  "lib/components/AudioVidStackPlayer.svelte": ["music-artist"],
  "lib/components/UniversalLightbox.svelte": ["direct"],
  "lib/components/VideoPlayer.svelte": ["direct", "hls"],
  "lib/components/collections/collection-item-helpers.ts": ["audio-track", "video-series"],
  "lib/components/entities/EntityGridFilterDrawer.svelte": [
    "audio-library", "audio-track", "book-chapter", "book-volume", "video-season", "video-series",
  ],
  "lib/components/files/FileDetailPane.svelte": [
    "audio-library", "audio-track", "book-chapter", "book-page", "video-season", "video-series",
  ],
  "lib/components/identify/identify-review-helpers.ts": ["audio-track"],
  "lib/components/identify/identify-store.svelte.ts": [
    "audio-library", "audio-track", "book-chapter", "book-volume", "music-artist", "video-season", "video-series",
  ],
  "lib/components/identify-review.ts": [
    "audio-library", "book-volume", "music-artist", "video-season", "video-series",
  ],
  "lib/components/settings/SettingsSectionPage.svelte": ["hls"],
  "lib/components/video-player-types.ts": ["direct"],
  "lib/entities/detail-lab-data.ts": ["audio-library", "video-series"],
  "lib/entities/thumbnail-lab-data.ts": [
    "audio-library", "audio-track", "book-chapter", "book-page", "book-volume", "video-season", "video-series",
  ],
  "lib/entities/video-capabilities.ts": ["direct"],
  "lib/jobs/helpers.ts": ["library-maintenance"],
  "lib/jobs/jobs-dashboard.ts": [
    "acquire-subtitles", "database-backup", "extract-subtitles", "library-maintenance", "monitored-search",
  ],
  "lib/jobs/models.ts": [
    "acquire-subtitles", "database-backup", "extract-subtitles", "library-maintenance", "monitored-search",
  ],
  "lib/jobs/run-catalog.ts": ["monitored-search"],
  "lib/player/media-badges.ts": ["direct"],
  "lib/player/quality-preference.ts": ["direct"],
  "lib/player/video-player-load.ts": ["direct", "hls"],
  "lib/player/video-player-source-policy.ts": ["direct", "hls"],
  "lib/search/entity-search.ts": ["direct"],
  "lib/search/models.ts": ["direct"],
  "lib/settings/app-settings.ts": ["direct", "hls"],
  "lib/settings/settings-section-catalog.ts": ["auto-identify"],
  "routes/+page.svelte": ["audio-library", "video-series"],
  "routes/artists/+page.svelte": ["music-artist"],
  "routes/artists/[id]/+page.svelte": ["audio-library"],
  "routes/audio/+page.svelte": ["audio-library"],
  "routes/audio/[id]/+page.svelte": ["audio-library", "audio-track", "music-artist"],
  "routes/authors/+page.svelte": ["book-author"],
  "routes/books/[id]/+page.svelte": ["book-author", "book-page"],
  "routes/movies/[id]/+page.svelte": ["direct", "hls"],
  "routes/series/+page.svelte": ["video-series"],
  "routes/videos/[id]/+page.svelte": ["direct", "hls"],
};

/**
 * Grandfathered oversized files, path → hard line ceiling (size at guard introduction
 * rounded up to the next 50). Splitting a file removes its entry; growing past the
 * ceiling fails. New files are capped at MAX_SOURCE_FILE_LINES.
 */
const OVERSIZED_FILE_CEILINGS: Record<string, number> = {
  "lib/components/entities/EntityDetail.svelte": 2900,
  "lib/components/VideoPlayer.svelte": 2100,
  "lib/components/thumbnails/EntityThumbnail.svelte": 1650,
  "routes/design-language/+page.svelte": 1450,
  "routes/books/[id]/+page.svelte": 1300,
  "lib/entities/entity-grid.ts": 1250,
  "lib/components/entities/EntityGridToolbar.svelte": 1250,
  "lib/components/entities/EntityGrid.svelte": 1250,
  "lib/components/identify/identify-store.svelte.ts": 1200,
  "lib/components/AudioVidStackPlayer.svelte": 1200,
  "lib/components/PdfReader.svelte": 1100,
};

const MAX_SOURCE_FILE_LINES = 1000;

function* walkSource(dir: string): Generator<string> {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    const rel = relative(SRC_ROOT, full).replaceAll("\\", "/");
    if (statSync(full).isDirectory()) {
      if (rel === "lib/api/generated" || rel === "test") continue;
      yield* walkSource(full);
    } else if (/\.(ts|svelte)$/.test(name) && !/\.(test|spec)\./.test(name)) {
      yield full;
    }
  }
}

function guardedValues(): Map<string, string> {
  const valueToFamily = new Map<string, string>();
  for (const [familyName, family] of Object.entries(GUARDED_FAMILIES)) {
    for (const value of Object.values(family)) {
      const distinctive =
        value.includes("-") || value.includes("_") || ALWAYS_GUARDED_FAMILIES.has(familyName);
      if (distinctive && !valueToFamily.has(value)) {
        valueToFamily.set(value, familyName);
      }
    }
  }
  return valueToFamily;
}

const STRING_LITERAL = /"([^"\\\n]*)"|'([^'\\\n]*)'|`([^`\\\n$]*)`/g;

describe("source discipline", () => {
  it("does not retype distinctive code values as string literals", () => {
    const guarded = guardedValues();
    const offenders: string[] = [];
    const seen = new Map<string, Set<string>>();

    for (const file of walkSource(SRC_ROOT)) {
      const rel = relative(SRC_ROOT, file).replaceAll("\\", "/");
      const text = readFileSync(file, "utf8");
      const allowed = new Set(MAGIC_CODE_ALLOWLIST[rel] ?? []);
      const hits = new Set<string>();
      for (const match of text.matchAll(STRING_LITERAL)) {
        const value = match[1] ?? match[2] ?? match[3];
        if (value !== undefined && guarded.has(value)) {
          hits.add(value);
        }
      }
      if (hits.size > 0) {
        seen.set(rel, hits);
      }
      for (const value of hits) {
        if (!allowed.has(value)) {
          offenders.push(`${rel}: "${value}" (${guarded.get(value)}) — import it from $lib/api/generated/codes`);
        }
      }
    }

    const stale: string[] = [];
    for (const [rel, values] of Object.entries(MAGIC_CODE_ALLOWLIST)) {
      const hits = seen.get(rel);
      for (const value of values) {
        if (!hits?.has(value)) {
          stale.push(`${rel}: "${value}" no longer occurs — remove it from MAGIC_CODE_ALLOWLIST`);
        }
      }
    }

    expect(offenders, `Retyped code literals:\n${offenders.join("\n")}`).toEqual([]);
    expect(stale, `Stale allowlist entries (ratchet down):\n${stale.join("\n")}`).toEqual([]);
  });

  it("keeps source files under the modularity ceiling", () => {
    const offenders: string[] = [];
    const stale: string[] = [];

    for (const file of walkSource(SRC_ROOT)) {
      const rel = relative(SRC_ROOT, file).replaceAll("\\", "/");
      const lines = readFileSync(file, "utf8").split("\n").length;
      const ceiling = OVERSIZED_FILE_CEILINGS[rel] ?? MAX_SOURCE_FILE_LINES;
      if (lines > ceiling) {
        offenders.push(`${rel}: ${lines} lines (ceiling ${ceiling}) — split it instead of growing it`);
      }
      if (rel in OVERSIZED_FILE_CEILINGS && lines <= MAX_SOURCE_FILE_LINES) {
        stale.push(`${rel}: now ${lines} lines — remove its OVERSIZED_FILE_CEILINGS entry`);
      }
    }

    expect(offenders, `Files exceed the modularity ceiling:\n${offenders.join("\n")}`).toEqual([]);
    expect(stale, `Stale ceilings (ratchet down):\n${stale.join("\n")}`).toEqual([]);
  });
});
