import { CAPABILITY_KIND, PROGRESS_UNIT, READER_MODE, type ProgressUnitCode, type ReaderModeCode } from "$lib/api/generated/codes";
import type { ImageListItemDto } from "@prismedia/contracts";
import { getCapability } from "$lib/api/capabilities";
import { numberValue } from "$lib/utils/format";
import type { EntityCard, EntityThumbnail } from "$lib/api/generated/model";

export interface BookReaderChapter {
  id: string;
  title: string;
  sortOrder: number;
  pageCount: number;
  startIndex?: number;
}

export interface BookEntityProgressDisplay {
  chapterId: string;
  chapterLabel: string;
  currentPage: number;
  pageCount: number;
  workPage: number;
  workTotal: number;
  percent: number;
  isComplete: boolean;
  showMeter: boolean;
  pageLabel: string | null;
  chapterPageLabel: string | null;
  workPageLabel: string | null;
  summaryLabel: string;
  detailLabel: string;
  readerMode: typeof READER_MODE.paged | typeof READER_MODE.webtoon;
}

export interface SingleFileBookProgressDisplay {
  percent: number;
  isComplete: boolean;
  positionLabel: string | null;
  unit: ProgressUnitCode;
  index: number;
  total: number;
  mode: ReaderModeCode;
  location: string | null;
}

/**
 * Builds the progress-panel display for single-file books (EPUB/PDF), which have no
 * chapter entities and so are skipped by {@link bookEntityProgressDisplay}.
 *
 * PDF readers save `unit: "page"` with a 0-based page `index` and a page-count `total`,
 * so a "Page N of M" position is meaningful. EPUB readers save `unit: "cfi"` with a
 * 0..`total` reading fraction (total = 10000), so only a percentage is meaningful.
 *
 * Returns null when the book has never been opened (no saved position) or when the
 * progress capability lacks a usable total.
 */
export function singleFileBookProgressDisplay(
  book: Pick<EntityCard, "capabilities"> | null | undefined,
): SingleFileBookProgressDisplay | null {
  const progress = book ? getCapability(book.capabilities, CAPABILITY_KIND.progress) : undefined;
  if (!progress?.currentEntityId) return null;

  const total = Math.max(0, numberValue(progress.total) ?? 0);
  if (total <= 0) return null;

  const index = Math.max(0, numberValue(progress.index) ?? 0);
  const isPaged = progress.unit === PROGRESS_UNIT.page;
  const rawPercent = isPaged
    ? Math.round(((Math.min(index, total - 1) + 1) / total) * 100)
    : Math.round((Math.min(index, total) / total) * 100);
  const isComplete = Boolean(progress.completedAt) || rawPercent >= 100;
  const percent = isComplete ? 100 : Math.min(100, Math.max(rawPercent > 0 ? 1 : 0, rawPercent));
  const currentPage = Math.min(index + 1, total);
  const positionLabel = isComplete
    ? null
    : isPaged
      ? `Page ${currentPage} of ${total}`
      : `${percent}% read`;

  return {
    percent,
    isComplete,
    positionLabel,
    unit: progress.unit,
    index,
    total,
    mode: progress.mode ?? (isPaged ? READER_MODE.scrolled : READER_MODE.paged),
    location: progress.location ?? null,
  };
}

export function orderedBookChildren(
  entity: Pick<EntityCard, "childrenByKind"> | null | undefined,
  kind: string,
): EntityThumbnail[] {
  const children = entity?.childrenByKind.find((group) => group.kind === kind)?.entities ?? [];
  return [...children].sort((a, b) => {
    const orderA = numberValue(a.sortOrder) ?? Number.MAX_SAFE_INTEGER;
    const orderB = numberValue(b.sortOrder) ?? Number.MAX_SAFE_INTEGER;
    return orderA - orderB || a.title.localeCompare(b.title) || a.id.localeCompare(b.id);
  });
}

export function entityPageToReaderImage(page: EntityThumbnail): ImageListItemDto {
  const sortOrder = numberValue(page.sortOrder) ?? 0;
  return {
    id: page.id,
    title: page.title,
    date: null,
    rating: numberValue(page.rating),
    organized: page.isOrganized,
    isNsfw: page.isNsfw,
    width: null,
    height: null,
    format: null,
    isVideo: false,
    fileSize: null,
    thumbnailPath: page.coverUrl,
    previewPath: null,
    fullPath: `/entities/${page.id}/files/source`,
    galleryId: null,
    sortOrder,
    studioId: null,
    performers: [],
    tags: [],
    createdAt: "",
  };
}

export function bookEntityProgressDisplay(
  book: Pick<EntityCard, "capabilities"> | null | undefined,
  chapters: BookReaderChapter[],
): BookEntityProgressDisplay | null {
  const progress = book ? getCapability(book.capabilities, CAPABILITY_KIND.progress) : undefined;
  if (!progress?.currentEntityId) return null;

  const chapter = chapters.find((item) => item.id === progress.currentEntityId);
  if (!chapter) return null;

  const pageCount = Math.max(0, chapter.pageCount || numberValue(progress.total) || 0);
  if (pageCount <= 0) return null;

  const localIndex = Math.max(0, numberValue(progress.index) ?? 0);
  const workTotal = Math.max(0, numberValue(progress.workTotal) ?? numberValue(progress.total) ?? pageCount);
  const workIndex = Math.max(0, numberValue(progress.workIndex) ?? (chapter.startIndex ?? 0) + localIndex);
  const currentPage = Math.min(localIndex + 1, pageCount);
  const workPage = workTotal > 0 ? Math.min(workIndex + 1, workTotal) : currentPage;
  const rawPercent = workTotal > 0
    ? Math.round(((Math.min(workIndex, workTotal - 1) + 1) / workTotal) * 100)
    : Math.round((currentPage / pageCount) * 100);
  const percent = Math.min(100, Math.max(workPage > 0 ? 1 : 0, rawPercent));
  const isComplete = Boolean(progress.completedAt) || percent >= 100;
  const chapterNumber = chapter.sortOrder + 1;
  const chapterLabel = `Ch. ${chapterNumber}: ${chapter.title}`;
  const pageLabel = isComplete ? null : `Page ${currentPage} of ${pageCount}`;
  const chapterPageLabel = isComplete ? null : `Chapter page ${currentPage} of ${pageCount}`;
  const workPageLabel = isComplete ? null : `Book page ${workPage} of ${workTotal}`;

  return {
    chapterId: chapter.id,
    chapterLabel,
    currentPage,
    pageCount,
    workPage,
    workTotal,
    percent,
    isComplete,
    showMeter: !isComplete,
    pageLabel,
    chapterPageLabel,
    workPageLabel,
    summaryLabel: isComplete ? `Read ${chapterLabel}` : `Reading ${chapterLabel} - ${pageLabel}`,
    detailLabel: isComplete ? "Read" : `${chapterPageLabel} · ${workPageLabel}`,
    readerMode: progress.mode === READER_MODE.webtoon ? READER_MODE.webtoon : READER_MODE.paged,
  };
}
