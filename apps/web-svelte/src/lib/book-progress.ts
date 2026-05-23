import type { BookChapterDto, BookProgressDto } from "@prismedia/contracts";

type BookProgressSource = {
  chapters: Pick<BookChapterDto, "id" | "title" | "chapterNumber" | "pageCount">[];
  progress: BookProgressDto | null;
};

type ChapterProgressSource = Pick<BookChapterDto, "id" | "title" | "chapterNumber" | "pageCount">;

export type BookChapterProgressDisplay = {
  chapterId: string;
  chapterLabel: string;
  currentPage: number;
  pageCount: number;
  percent: number;
  isComplete: boolean;
  showMeter: boolean;
  pageLabel: string | null;
  summaryLabel: string;
  detailLabel: string;
};

export function getCurrentChapterProgressDisplay(book: BookProgressSource): BookChapterProgressDisplay | null {
  const chapter = book.progress?.chapterId
    ? book.chapters.find((item) => item.id === book.progress?.chapterId)
    : null;
  return chapter ? buildProgressDisplay(book.progress, chapter) : null;
}

export function getChapterProgressDisplay(
  book: BookProgressSource,
  chapter: ChapterProgressSource,
): BookChapterProgressDisplay | null {
  if (book.progress?.chapterId !== chapter.id) return null;
  return buildProgressDisplay(book.progress, chapter);
}

function buildProgressDisplay(
  progress: BookProgressDto | null,
  chapter: ChapterProgressSource,
): BookChapterProgressDisplay | null {
  if (!progress) return null;
  const pageCount = Math.max(0, Math.round(progress.pageCount || chapter.pageCount));
  if (pageCount <= 0) return null;
  const currentPage = Math.min(Math.max(1, Math.round(progress.pageIndex) + 1), pageCount);
  const percent = Math.min(100, Math.max(0, Math.round((currentPage / pageCount) * 100)));
  const isComplete = Boolean(progress.completedAt);
  const chapterLabel = `Ch. ${chapter.chapterNumber}: ${chapter.title}`;
  const pageLabel = isComplete ? null : `Page ${currentPage} of ${pageCount}`;

  return {
    chapterId: chapter.id,
    chapterLabel,
    currentPage,
    pageCount,
    percent,
    isComplete,
    showMeter: !isComplete,
    pageLabel,
    summaryLabel: isComplete ? `Read ${chapterLabel}` : `Reading ${chapterLabel} - ${pageLabel}`,
    detailLabel: isComplete ? "Read" : `Reading progress - ${pageLabel}`,
  };
}
