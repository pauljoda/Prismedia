import { apiAssetUrl } from "$lib/api/orval-fetch";

export interface EpubTocSourceEntry {
  label?: unknown;
  href?: unknown;
  subitems?: unknown;
}

export interface EpubContentsEntry {
  id: string;
  title: string;
  location: string;
  depth: number;
  order: number;
  sectionIndex: number | null;
}

export interface EpubBookNavigation {
  resolveHref: (href: string) => { index?: unknown } | null | undefined;
  resolveCFI: (cfi: string) => { index?: unknown } | null | undefined;
}

interface EpubBook extends EpubBookNavigation {
  toc?: unknown;
  destroy?: () => void;
}

export interface LoadedEpubContents {
  entries: EpubContentsEntry[];
  currentChapterId: string | null;
}

function sourceChildren(value: unknown): EpubTocSourceEntry[] {
  return Array.isArray(value) ? value as EpubTocSourceEntry[] : [];
}

/** Converts Foliate's nested EPUB navigation into actionable preorder rows. */
export function flattenEpubToc(
  items: unknown,
  navigation?: Pick<EpubBookNavigation, "resolveHref">,
): EpubContentsEntry[] {
  const entries: EpubContentsEntry[] = [];

  function visit(sourceItems: EpubTocSourceEntry[], depth: number) {
    for (const item of sourceItems) {
      const title = typeof item.label === "string" ? item.label.trim() : "";
      const location = typeof item.href === "string" ? item.href.trim() : "";
      if (title && location) {
        const resolvedIndex = Number(navigation?.resolveHref(location)?.index);
        entries.push({
          id: location,
          title,
          location,
          depth,
          order: entries.length,
          sectionIndex: Number.isInteger(resolvedIndex) && resolvedIndex >= 0 ? resolvedIndex : null,
        });
      }
      visit(sourceChildren(item.subitems), depth + 1);
    }
  }

  visit(sourceChildren(items), 0);
  const deepestByLocation = new Map<string, EpubContentsEntry>();
  for (const entry of entries) {
    const current = deepestByLocation.get(entry.location);
    if (!current || current.depth < entry.depth) deepestByLocation.set(entry.location, entry);
  }
  return entries
    .filter((entry) => deepestByLocation.get(entry.location) === entry)
    .map((entry, order) => ({ ...entry, order }));
}

/** Finds the TOC row that owns the section containing the persisted EPUB CFI. */
export function resolveCurrentEpubChapter(
  entries: readonly EpubContentsEntry[],
  location: string | null | undefined,
  navigation: EpubBookNavigation,
): EpubContentsEntry | null {
  if (!location) return null;
  const resolved = location.startsWith("epubcfi(")
    ? navigation.resolveCFI(location)
    : navigation.resolveHref(location);
  const currentIndex = Number(resolved?.index);
  if (!Number.isInteger(currentIndex) || currentIndex < 0) return null;

  return entries.reduce<EpubContentsEntry | null>((current, entry) => {
    if (entry.sectionIndex === null || entry.sectionIndex > currentIndex) return current;
    if (!current || (current.sectionIndex ?? -1) <= entry.sectionIndex) return entry;
    return current;
  }, null);
}

/** Loads just enough of an EPUB with the vendored reader parser to expose its TOC on detail pages. */
export async function loadEpubContents(
  sourceUrl: string,
  currentLocation?: string | null,
  signal?: AbortSignal,
): Promise<LoadedEpubContents> {
  const absoluteUrl = apiAssetUrl(sourceUrl) ?? sourceUrl;
  const response = await fetch(absoluteUrl, { signal });
  if (!response.ok) throw new Error(`Failed to load book contents (${response.status})`);
  const file = new File([await response.blob()], "book.epub", { type: "application/epub+zip" });
  if (signal?.aborted) throw new DOMException("Aborted", "AbortError");

  const { makeBook } = await import("$lib/vendor/foliate-js/view.js");
  const book = await makeBook(file) as EpubBook;
  try {
    const entries = flattenEpubToc(book.toc, book);
    const current = resolveCurrentEpubChapter(entries, currentLocation, book);
    return { entries, currentChapterId: current?.id ?? null };
  } finally {
    book.destroy?.();
  }
}
