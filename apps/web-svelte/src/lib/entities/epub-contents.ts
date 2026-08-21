import type { BookContentsEntry } from "$lib/api/generated/model";

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
  startFraction?: number | null;
  endFraction?: number | null;
}

export interface EpubSectionSource {
  size?: unknown;
  linear?: unknown;
}

export interface EpubBookNavigation {
  resolveHref: (href: string) => { index?: unknown } | null | undefined;
  resolveCFI: (cfi: string) => { index?: unknown } | null | undefined;
}

/** Adds whole-book fraction bounds to each TOC row using Foliate's section-size model. */
export function addEpubChapterRanges(
  entries: readonly EpubContentsEntry[],
  sections: readonly EpubSectionSource[],
): EpubContentsEntry[] {
  const sizes = sections.map((section) => {
    const size = Number(section.size);
    return section.linear !== "no" && Number.isFinite(size) && size > 0 ? size : 0;
  });
  const total = sizes.reduce((sum, size) => sum + size, 0);
  if (total <= 0) return entries.map((entry) => ({ ...entry }));

  const sectionFractions = [0];
  let accumulatedSize = 0;
  for (const size of sizes) {
    accumulatedSize += size;
    sectionFractions.push(accumulatedSize / total);
  }

  return entries.map((entry, index) => {
    const sectionIndex = entry.sectionIndex;
    if (sectionIndex === null || sectionIndex < 0 || sectionIndex >= sizes.length) return { ...entry };
    const nextSectionIndex = entries
      .slice(index + 1)
      .map((candidate) => candidate.sectionIndex)
      .find((candidate): candidate is number => candidate !== null && candidate > sectionIndex);
    const startFraction = sectionFractions[sectionIndex] ?? 0;
    const endFraction = nextSectionIndex === undefined
      ? 1
      : sectionFractions[nextSectionIndex] ?? 1;
    return endFraction > startFraction
      ? { ...entry, startFraction, endFraction }
      : { ...entry };
  });
}

/** Keeps exact web EPUB cursors; native/foreign locator formats resume via canonical fraction. */
export function exactWebEpubResumeLocation(location: string | null | undefined): string | null {
  const normalized = location?.trim() ?? "";
  return normalized.toLowerCase().startsWith("epubcfi(") ? normalized : null;
}

/** Keeps Foliate navigation targets while rejecting native-only saved locator formats. */
export function webEpubLaunchLocation(location: string | null | undefined): string | null {
  const normalized = location?.trim() ?? "";
  if (!normalized) return null;
  if (normalized.startsWith("{") || normalized.startsWith("[")) return null;
  if (normalized.includes("#prismedia-progress=")) return null;
  return normalized;
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

/** Finds the TOC row whose chapter-scoped range owns a normalized whole-book position. */
export function resolveEpubChapterByFraction(
  entries: readonly EpubContentsEntry[],
  fraction: number | null | undefined,
): EpubContentsEntry | null {
  if (typeof fraction !== "number" || !Number.isFinite(fraction)) return null;
  const normalized = Math.max(0, Math.min(1, fraction));
  return entries.findLast((entry) =>
    typeof entry.startFraction === "number" &&
    typeof entry.endFraction === "number" &&
    normalized >= entry.startFraction &&
    normalized <= entry.endFraction
  ) ?? null;
}

function numberOrNull(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || value === "") return null;
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : null;
}

/** Normalizes server-projected readable-chapter entries into typed rows. */
export function mapBookContentsEntries(
  items: readonly BookContentsEntry[],
): EpubContentsEntry[] {
  return items.map((entry): EpubContentsEntry => ({
    id: entry.id,
    title: entry.title,
    location: entry.location,
    depth: numberOrNull(entry.depth) ?? 0,
    order: numberOrNull(entry.order) ?? 0,
    sectionIndex: numberOrNull(entry.sectionIndex),
    startFraction: numberOrNull(entry.startFraction),
    endFraction: numberOrNull(entry.endFraction),
  }));
}

/**
 * Resolves which chapter owns the reader's saved position — an exact location match when the
 * cursor is a plain navigation target, else the chapter whose fraction range contains it. Pure
 * math over already-loaded entries, so progress changes never require refetching contents.
 */
export function resolveCurrentContentsEntry(
  entries: readonly EpubContentsEntry[],
  currentLocation?: string | null,
  currentFraction?: number | null,
): EpubContentsEntry | null {
  const normalizedLocation = currentLocation?.trim() ?? "";
  const current = normalizedLocation && !normalizedLocation.toLowerCase().startsWith("epubcfi(")
    ? entries.findLast((entry) => entry.location === normalizedLocation) ?? null
    : null;
  return current ?? resolveEpubChapterByFraction(entries, currentFraction);
}
