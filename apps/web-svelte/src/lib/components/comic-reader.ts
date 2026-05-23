export type ComicPageMode = "single" | "double";
export type ComicTapZone = "previous" | "controls" | "next";

export interface ComicReaderOptions {
  pageMode: ComicPageMode;
  firstPageIsCover: boolean;
}

function clampIndex(index: number, total: number) {
  if (total <= 0) return 0;
  return Math.max(0, Math.min(total - 1, index));
}

export function comicSpreadForIndex(
  index: number,
  total: number,
  options: ComicReaderOptions,
): number[] {
  if (total <= 0) return [];
  const current = clampIndex(index, total);
  if (options.pageMode === "single") return [current];
  if (options.firstPageIsCover && current === 0) return [0];

  const spreadStart = options.firstPageIsCover
    ? current % 2 === 1
      ? current
      : current - 1
    : current % 2 === 0
      ? current
      : current - 1;
  const safeStart = clampIndex(spreadStart, total);
  const next = safeStart + 1;
  return next < total ? [safeStart, next] : [safeStart];
}

export function nextComicIndex(
  index: number,
  total: number,
  options: ComicReaderOptions,
): number {
  if (total <= 0) return 0;
  if (options.pageMode === "single") return clampIndex(index + 1, total);
  const spread = comicSpreadForIndex(index, total, options);
  const next = (spread.at(-1) ?? index) + 1;
  return clampIndex(next, total);
}

export function previousComicIndex(
  index: number,
  total: number,
  options: ComicReaderOptions,
): number {
  if (total <= 0) return 0;
  if (options.pageMode === "single") return clampIndex(index - 1, total);
  const spread = comicSpreadForIndex(index, total, options);
  const previous = (spread[0] ?? index) - 1;
  if (options.firstPageIsCover && previous <= 0) return 0;
  if (!options.firstPageIsCover) return previous % 2 === 0 ? clampIndex(previous, total) : clampIndex(previous - 1, total);
  return previous % 2 === 1 ? clampIndex(previous, total) : clampIndex(previous - 1, total);
}

export function comicTapZone(x: number, width: number): ComicTapZone {
  if (width <= 0) return "controls";
  const ratio = x / width;
  if (ratio < 1 / 3) return "previous";
  if (ratio > 2 / 3) return "next";
  return "controls";
}

export function comicPreloadIndexes(
  index: number,
  total: number,
  options: ComicReaderOptions,
  radius = 2,
): number[] {
  const visible = comicSpreadForIndex(index, total, options);
  if (visible.length === 0) return [];

  const visibleSet = new Set(visible);
  const start = clampIndex(Math.min(...visible) - radius, total);
  const end = clampIndex(Math.max(...visible) + radius, total);
  const indexes: number[] = [];

  for (let i = start; i <= end; i += 1) {
    if (!visibleSet.has(i)) indexes.push(i);
  }

  return indexes;
}
