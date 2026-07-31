import type { EntityCard } from "$lib/api/generated/model";
import {
  fetchEntityChildren,
  type EntityThumbnailRequestOptions,
} from "$lib/api/entities";
import { orderedBookChildren, type BookReaderChapter } from "$lib/entities/book-entity-reader";
import { ENTITY_KIND } from "$lib/entities/entity-codes";

/**
 * Resolves the current chapter's ordered sibling context with one batch projection for every
 * volume. Only the current chapter needs a page count for next-chapter navigation and progress.
 */
export async function fetchBookChapterSummaries(
  book: EntityCard,
  currentChapter: EntityCard,
  options?: EntityThumbnailRequestOptions,
): Promise<BookReaderChapter[]> {
  const currentPageCount = orderedBookChildren(currentChapter, ENTITY_KIND.bookPage).length;
  const volumeThumbnails = orderedBookChildren(book, ENTITY_KIND.bookVolume);
  if (volumeThumbnails.length === 0) {
    return summariesFor(
      orderedBookChildren(book, ENTITY_KIND.bookChapter),
      currentChapter.id,
      currentPageCount,
    );
  }

  const volumeGroups = await fetchEntityChildren(
    volumeThumbnails.map((volume) => volume.id),
    options,
  );
  const chaptersByVolume = new Map(volumeGroups.map((group) => [
    group.parentId,
    group.items.filter((child) => child.kind === ENTITY_KIND.bookChapter),
  ]));
  const parentVolumeIndex = resolveParentVolumeIndex(
    volumeThumbnails,
    chaptersByVolume,
    currentChapter,
  );
  if (parentVolumeIndex < 0) {
    return summariesFor(
      orderedBookChildren(book, ENTITY_KIND.bookChapter),
      currentChapter.id,
      currentPageCount,
    );
  }

  const currentVolumeId = volumeThumbnails[parentVolumeIndex]?.id;
  let chapterThumbnails = currentVolumeId
    ? [...(chaptersByVolume.get(currentVolumeId) ?? [])]
    : [];
  const currentIndex = chapterThumbnails.findIndex((chapter) => chapter.id === currentChapter.id);
  if (currentIndex === chapterThumbnails.length - 1) {
    const nextVolumeId = volumeThumbnails[parentVolumeIndex + 1]?.id;
    if (nextVolumeId) {
      chapterThumbnails = [
        ...chapterThumbnails,
        ...(chaptersByVolume.get(nextVolumeId) ?? []),
      ];
    }
  }

  return summariesFor(chapterThumbnails, currentChapter.id, currentPageCount);
}

function resolveParentVolumeIndex(
  volumes: ReturnType<typeof orderedBookChildren>,
  chaptersByVolume: ReadonlyMap<string, ReturnType<typeof orderedBookChildren>>,
  currentChapter: EntityCard,
): number {
  const structuralIndex = volumes.findIndex((volume) => volume.id === currentChapter.parentEntityId);
  if (structuralIndex >= 0) return structuralIndex;

  return volumes.findIndex((volume) =>
    chaptersByVolume.get(volume.id)?.some((chapter) => chapter.id === currentChapter.id) === true,
  );
}

function summariesFor(
  chapters: ReturnType<typeof orderedBookChildren>,
  currentChapterId: string,
  currentPageCount: number,
): BookReaderChapter[] {
  return chapters.map((thumbnail, index) => ({
    id: thumbnail.id,
    title: thumbnail.title,
    sortOrder: index,
    pageCount: thumbnail.id === currentChapterId ? currentPageCount : 0,
  }));
}
