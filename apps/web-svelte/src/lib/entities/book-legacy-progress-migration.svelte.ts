import { CAPABILITY_KIND } from "$lib/api/generated/codes";
import type { BookProgressTrackMapping } from "$lib/api/generated/model";
import type { BookDetail } from "$lib/api/media";
import { updateEntityProgress } from "$lib/api/playback";
import { getCapability } from "$lib/api/capabilities";
import type { BookChapterRow } from "$lib/entities/book-chapter-list";
import {
  bookProgressCursor,
  legacyBookProgressPromotion,
  shouldPromoteLegacyBookProgress,
} from "$lib/entities/book-combined-progress";

/** Promotes the farther pre-unification audiobook resume cursor once mappings are available. */
export function useLegacyBookProgressMigration(
  getBook: () => BookDetail | null,
  getRows: () => readonly BookChapterRow[],
  getMappings: () => readonly BookProgressTrackMapping[],
  refresh: (bookId: string, options: { showLoading: boolean }) => Promise<void>,
): void {
  let migrationKey: string | null = null;

  $effect(() => {
    const book = getBook();
    if (!book) return;
    const playback = getCapability(book.capabilities, CAPABILITY_KIND.playback);
    const progress = getCapability(book.capabilities, CAPABILITY_KIND.progress);
    const mappings = getMappings();
    const promotion = legacyBookProgressPromotion(
      getRows(),
      mappings,
      Number(playback?.resumeSeconds ?? 0),
    );
    if (progress?.completedAt || !shouldPromoteLegacyBookProgress(
      mappings,
      bookProgressCursor(progress),
      promotion,
    ) || !promotion) return;

    const key = `${book.id}:${promotion.mapping.trackId}:${promotion.update.index}`;
    if (migrationKey === key) return;
    migrationKey = key;
    void updateEntityProgress(book.id, promotion.update)
      .then(() => refresh(book.id, { showLoading: false }))
      .catch(() => {
        if (migrationKey === key) migrationKey = null;
      });
  });
}
