import { describe, expect, it } from "vitest";
import {
  ACQUISITION_STATUS,
  BOOK_RENDITION,
  ENTITY_KIND,
  MONITOR_STATUS,
} from "$lib/api/generated/codes";
import type { AcquisitionDetail, MonitorView } from "$lib/api/generated/model";
import {
  bookRenditionCanRequest,
  bookRenditionRows,
} from "$lib/requests/book-rendition-acquisition";

describe("Book rendition acquisition", () => {
  it("keeps ebook and audiobook acquisition stories visible on the same Book", () => {
    const rows = bookRenditionRows(
      [acquisition("ebook-acquisition", BOOK_RENDITION.ebook), acquisition("audio-acquisition", BOOK_RENDITION.audiobook)],
      [monitor("ebook-monitor", BOOK_RENDITION.ebook), monitor("audio-monitor", BOOK_RENDITION.audiobook)],
      { ebook: true, audiobook: false },
    );

    expect(rows).toMatchObject([
      {
        rendition: BOOK_RENDITION.ebook,
        owned: true,
        acquisition: { summary: { id: "ebook-acquisition" } },
        monitor: { id: "ebook-monitor" },
      },
      {
        rendition: BOOK_RENDITION.audiobook,
        owned: false,
        acquisition: { summary: { id: "audio-acquisition" } },
        monitor: { id: "audio-monitor" },
      },
    ]);
  });

  it("treats a legacy Book acquisition without a rendition as the ebook row", () => {
    const legacy = acquisition("legacy", BOOK_RENDITION.ebook);
    legacy.summary.bookRendition = undefined;

    expect(bookRenditionRows([legacy], [], { ebook: true, audiobook: false })[0].acquisition?.summary.id)
      .toBe("legacy");
  });

  it("reopens only terminal missing rendition history for a new request", () => {
    const cancelled = acquisition("cancelled", BOOK_RENDITION.audiobook);
    cancelled.summary.status = ACQUISITION_STATUS.cancelled;
    const importedWithoutFile = acquisition("imported", BOOK_RENDITION.audiobook);
    const failed = acquisition("failed", BOOK_RENDITION.audiobook);
    failed.summary.status = ACQUISITION_STATUS.failed;

    expect(bookRenditionCanRequest({
      rendition: BOOK_RENDITION.audiobook,
      owned: false,
      acquisition: cancelled,
      monitor: null,
    })).toBe(true);
    expect(bookRenditionCanRequest({
      rendition: BOOK_RENDITION.audiobook,
      owned: false,
      acquisition: importedWithoutFile,
      monitor: null,
    })).toBe(true);
    expect(bookRenditionCanRequest({
      rendition: BOOK_RENDITION.audiobook,
      owned: false,
      acquisition: failed,
      monitor: null,
    })).toBe(false);
  });

});

function acquisition(
  id: string,
  bookRendition: typeof BOOK_RENDITION[keyof typeof BOOK_RENDITION],
): AcquisitionDetail {
  return {
    summary: {
      id,
      status: ACQUISITION_STATUS.imported,
      statusMessage: null,
      title: "A Game of Thrones",
      author: "George R. R. Martin",
      series: "A Song of Ice and Fire",
      year: 1996,
      posterUrl: null,
      progress: 1,
      createdAt: "2026-07-12T00:00:00Z",
      updatedAt: "2026-07-12T00:00:00Z",
      kind: ENTITY_KIND.book,
      entityId: "book-1",
      bookRendition,
    },
    candidates: [],
  };
}

function monitor(
  id: string,
  bookRendition: typeof BOOK_RENDITION[keyof typeof BOOK_RENDITION],
): MonitorView {
  return {
    id,
    kind: ENTITY_KIND.book,
    acquisitionId: null,
    status: MONITOR_STATUS.active,
    title: "A Game of Thrones",
    author: "George R. R. Martin",
    acquisitionStatus: null,
    createdAt: "2026-07-12T00:00:00Z",
    updatedAt: "2026-07-12T00:00:00Z",
    entityId: "book-1",
    bookRendition,
  };
}
