/**
 * Batch identification — fan-out for non-batch plugins, episode matching.
 */

import type {
  PrismediaPluginManifest,
  PluginInput,
  EpisodeMapping,
} from "./types";

// ─── Episode Filename Matching ─────────────────────────────────────

/**
 * Pattern priority: S01E03 → 1x03 → E03 → standalone 2-3 digit number at word boundary
 */
const EPISODE_PATTERNS = [
  /[Ss](\d+)[Ee](\d+)/,     // S01E03
  /(\d+)[xX](\d+)/,         // 1x03
  /[Ee](\d+)/,              // E03
  /(?:^|[\s._-])(\d{2,3})(?=$|[\s._-])/, // standalone 03 or 003
];

export interface EpisodeMatch {
  sceneId: string;
  filename: string;
  /** Parsed episode number from filename, or null if positional assignment */
  parsedEpisodeNumber: number | null;
  parsedSeasonNumber: number | null;
}

/**
 * Extract episode/season numbers from a filename.
 */
export function parseEpisodeFromFilename(
  filename: string,
): { episode: number | null; season: number | null } {
  // S01E03 pattern
  const sxeMatch = filename.match(/[Ss](\d+)[Ee](\d+)/);
  if (sxeMatch) {
    return {
      season: parseInt(sxeMatch[1], 10),
      episode: parseInt(sxeMatch[2], 10),
    };
  }

  // 1x03 pattern
  const xMatch = filename.match(/(\d+)[xX](\d+)/);
  if (xMatch) {
    return {
      season: parseInt(xMatch[1], 10),
      episode: parseInt(xMatch[2], 10),
    };
  }

  // E03 pattern
  const eMatch = filename.match(/[Ee](\d+)/);
  if (eMatch) {
    return {
      season: null,
      episode: parseInt(eMatch[1], 10),
    };
  }

  // Standalone 2-3 digit number
  const numMatch = filename.match(/(?:^|[\s._-])(\d{2,3})(?=$|[\s._-])/);
  if (numMatch) {
    return {
      season: null,
      episode: parseInt(numMatch[1], 10),
    };
  }

  return { season: null, episode: null };
}

/**
 * Natural sort comparator for filenames.
 * Handles numeric portions correctly (e.g. "Episode 2" before "Episode 10").
 */
function naturalCompare(a: string, b: string): number {
  const ax = a.split(/(\d+)/);
  const bx = b.split(/(\d+)/);
  const len = Math.min(ax.length, bx.length);

  for (let i = 0; i < len; i++) {
    const ai = ax[i];
    const bi = bx[i];
    if (ai === bi) continue;

    // If both are numeric segments, compare as numbers
    if (/^\d+$/.test(ai) && /^\d+$/.test(bi)) {
      const diff = parseInt(ai, 10) - parseInt(bi, 10);
      if (diff !== 0) return diff;
    }

    return ai.localeCompare(bi, undefined, { sensitivity: "base" });
  }

  return ax.length - bx.length;
}

export interface SceneFileInfo {
  sceneId: string;
  filePath: string;
  filename: string;
}

/**
 * Match scenes in a folder to episodes from a provider's episode list.
 *
 * Strategy:
 * 1. Parse episode numbers from each filename
 * 2. For scenes with parsed numbers, match exactly against the episode map
 * 3. For scenes without parsed numbers, assign positionally (natural sort order)
 *
 * @returns Matched and unmatched scenes
 */
export function matchScenesToEpisodes(
  scenes: SceneFileInfo[],
  episodes: EpisodeMapping[],
): {
  matched: Array<{
    sceneId: string;
    filename: string;
    episode: EpisodeMapping;
  }>;
  unmatched: SceneFileInfo[];
} {
  // Sort scenes by filename (natural sort)
  const sorted = [...scenes].sort((a, b) =>
    naturalCompare(a.filename, b.filename),
  );

  // Build episode lookup by episode number
  const episodeByNumber = new Map<number, EpisodeMapping>();
  for (const ep of episodes) {
    episodeByNumber.set(ep.episodeNumber, ep);
  }

  const matched: Array<{
    sceneId: string;
    filename: string;
    episode: EpisodeMapping;
  }> = [];
  const unmatchedScenes: SceneFileInfo[] = [];
  const usedEpisodes = new Set<number>();

  // First pass: exact episode number matching from filenames
  for (const scene of sorted) {
    const parsed = parseEpisodeFromFilename(scene.filename);
    if (parsed.episode != null) {
      const ep = episodeByNumber.get(parsed.episode);
      if (ep && !usedEpisodes.has(ep.episodeNumber)) {
        matched.push({
          sceneId: scene.sceneId,
          filename: scene.filename,
          episode: ep,
        });
        usedEpisodes.add(ep.episodeNumber);
        continue;
      }
    }
    unmatchedScenes.push(scene);
  }

  // Second pass: positional assignment for remaining scenes
  const remainingEpisodes = episodes
    .filter((ep) => !usedEpisodes.has(ep.episodeNumber))
    .sort((a, b) => a.episodeNumber - b.episodeNumber);

  const stillUnmatched: SceneFileInfo[] = [];
  for (let i = 0; i < unmatchedScenes.length; i++) {
    if (i < remainingEpisodes.length) {
      matched.push({
        sceneId: unmatchedScenes[i].sceneId,
        filename: unmatchedScenes[i].filename,
        episode: remainingEpisodes[i],
      });
    } else {
      stillUnmatched.push(unmatchedScenes[i]);
    }
  }

  // Sort matched by episode number for consistent ordering
  matched.sort((a, b) => a.episode.episodeNumber - b.episode.episodeNumber);

  return { matched, unmatched: stillUnmatched };
}

/**
 * Fan out single-item plugin calls when the plugin doesn't support batch mode.
 *
 * @param concurrency Maximum parallel executions (default 3)
 */
export async function fanOut<TInput, TResult>(
  items: Array<{ id: string; input: TInput }>,
  executor: (input: TInput) => Promise<TResult | null>,
  concurrency = 3,
): Promise<Array<{ id: string; result: TResult | null }>> {
  const results: Array<{ id: string; result: TResult | null }> = [];
  const queue = [...items];

  async function worker() {
    while (queue.length > 0) {
      const item = queue.shift()!;
      try {
        const result = await executor(item.input);
        results.push({ id: item.id, result });
      } catch {
        results.push({ id: item.id, result: null });
      }
    }
  }

  const workers = Array.from(
    { length: Math.min(concurrency, items.length) },
    () => worker(),
  );
  await Promise.all(workers);

  return results;
}
