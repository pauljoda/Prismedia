/**
 * Fetch and cache the Prismedia Community Plugin Index.
 */

import yaml from "js-yaml";
import type { PluginIndexEntry } from "./types";

let indexCache: { data: PluginIndexEntry[]; fetchedAt: number } | null = null;
const INDEX_CACHE_TTL = 5 * 60 * 1000; // 5 minutes

/**
 * Resolve a user-supplied registry URL to the concrete `index.yml`
 * location. Callers may pass the base directory (e.g. the
 * `raw.githubusercontent.com/.../main` URL from `.env.example`) or the
 * full file URL; both should work.
 */
export function resolveIndexUrl(base: string): string {
  if (/\.(ya?ml)(\?|#|$)/i.test(base)) return base;
  return base.replace(/\/+$/, "") + "/index.yml";
}

/**
 * Resolve a plugin entry's relative `path` field (e.g.
 * `plugins/tmdb/tmdb.zip`) against the registry base URL so callers
 * get an absolute URL they can pass to `fetch`.
 */
export function resolveEntryZipUrl(
  base: string,
  entryPath: string,
): string {
  if (/^https?:\/\//i.test(entryPath)) return entryPath;
  const root = base.replace(/\/index\.ya?ml(\?.*)?$/i, "").replace(/\/+$/, "");
  return `${root}/${entryPath.replace(/^\/+/, "")}`;
}

/**
 * Fetch the community plugin index from the given URL.
 * Returns cached data if still fresh.
 */
export async function fetchPluginIndex(
  indexUrl: string,
  force = false,
): Promise<PluginIndexEntry[]> {
  if (
    !force &&
    indexCache &&
    Date.now() - indexCache.fetchedAt < INDEX_CACHE_TTL
  ) {
    return indexCache.data;
  }

  const resolved = resolveIndexUrl(indexUrl);
  const res = await fetch(resolved);
  if (!res.ok) {
    throw new Error(
      `Failed to fetch plugin index (${resolved}): ${res.status} ${res.statusText}`,
    );
  }

  const text = await res.text();
  const entries = yaml.load(text, { schema: yaml.JSON_SCHEMA });

  if (!Array.isArray(entries)) {
    throw new Error("Invalid plugin index format — expected array");
  }

  const validated: PluginIndexEntry[] = entries
    .filter((e: unknown) => e && typeof e === "object")
    .map((e: Record<string, unknown>) => ({
      id: String(e.id ?? ""),
      name: String(e.name ?? ""),
      version: String(e.version ?? ""),
      date: String(e.date ?? ""),
      path: String(e.path ?? ""),
      sha256: String(e.sha256 ?? ""),
      runtime: (String(e.runtime ?? "python") as PluginIndexEntry["runtime"]),
      isNsfw: e.isNsfw === true,
      capabilities: (e.capabilities as Record<string, boolean>) ?? {},
      description:
        typeof e.description === "string" ? e.description : undefined,
      author: typeof e.author === "string" ? e.author : undefined,
      requires: Array.isArray(e.requires)
        ? e.requires.map(String)
        : undefined,
    }));

  indexCache = { data: validated, fetchedAt: Date.now() };
  return validated;
}

/**
 * Invalidate the cached plugin index.
 */
export function clearPluginIndexCache(): void {
  indexCache = null;
}
