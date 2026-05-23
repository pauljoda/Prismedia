import type { ImageListItemDto } from "@prismedia/contracts";
import { getCapability } from "$lib/api/capabilities";
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
  readerMode: "paged" | "webtoon";
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
  const progress = book ? getCapability(book.capabilities, "progress") : undefined;
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
  const isComplete = Boolean(progress.completedAt);
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
    readerMode: progress.mode === "webtoon" ? "webtoon" : "paged",
  };
}

function numberValue(value: number | string | null | undefined): number | null {
  if (typeof value === "number") return Number.isFinite(value) ? value : null;
  if (typeof value !== "string" || value.trim() === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}
