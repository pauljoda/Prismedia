export interface SeriesLoadedWindowArgs {
  loadedStart: number;
  itemCount: number;
  total: number;
  pageSize: number;
}

export function getSeriesLoadedWindow(args: SeriesLoadedWindowArgs) {
  const loadedEnd = Math.min(args.total, args.loadedStart + args.itemCount);
  return {
    loadedEnd,
    hasMore: loadedEnd < args.total,
    nextPageNumber:
      Math.floor((args.loadedStart + args.itemCount) / args.pageSize) + 1,
  };
}

export function seriesPageHref(url: URL, nextPage: number): string {
  const params = new URLSearchParams(url.searchParams);
  if (nextPage > 1) params.set("page", String(nextPage));
  else params.delete("page");
  const qs = params.toString();
  return qs ? `${url.pathname}?${qs}` : url.pathname;
}

export function applySeriesPageMerge<T extends { id: string }>(args: {
  current: T[];
  incoming: T[];
  loadedStart: number;
  total: number;
}) {
  const existing = new Set(args.current.map((item) => item.id));
  const next = args.incoming.filter((item) => !existing.has(item.id));
  const items = [...args.current, ...next];

  return {
    added: next.length,
    items,
    total: next.length === 0 ? args.loadedStart + args.current.length : args.total,
  };
}
