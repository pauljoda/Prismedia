import type { NsfwMode } from "./cookie";

/**
 * In SFW (off) mode, NSFW-tagged rows are omitted from chip rows so only
 * safe tags show real names. Show mode keeps the full list.
 */
export function tagsVisibleInNsfwMode<T extends { isNsfw?: boolean }>(
  tags: T[] | undefined,
  mode: NsfwMode,
): T[] {
  if (!tags?.length) return [];
  if (mode === "show") return tags;
  return tags.filter((t) => t.isNsfw !== true);
}
