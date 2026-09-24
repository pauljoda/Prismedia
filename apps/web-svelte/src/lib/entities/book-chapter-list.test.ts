import { describe, expect, it } from "vitest";
import {
  ALIGNMENT_GAP_REASON,
  AUDIOBOOK_STRUCTURE,
  BOOK_CHAPTER_MAPPING_ORIGIN,
  BOOK_LINK_STATE,
  CONSUMPTION_MODALITY,
  type BookLinkStateCode,
} from "$lib/api/generated/codes";
import type { AudioChapterWindow, BookAlignmentResponse, ReadableChapterWindow } from "$lib/api/generated/model";
import { audioWindowKey, bookSeparateProgress, sequentialBookChapterMappings } from "./book-chapter-list";

function readable(key: string, title: string): ReadableChapterWindow {
  return {
    chapterKey: key,
    title,
    depth: 0,
    location: `Text/${key}.xhtml`,
    chapterEntityId: null,
    startFraction: null,
    endFraction: null,
    pageCount: null,
  };
}

function audio(trackId: string, markerId: string | null, title: string, start: number, end: number) {
  const window: AudioChapterWindow = {
    trackEntityId: trackId,
    markerId,
    title,
    startSeconds: start,
    endSeconds: end,
    endInferred: false,
  };
  return { key: audioWindowKey(trackId, markerId), window };
}

describe("book chapter list", () => {
  it("maps audio windows sequentially from the chapter the user marks first", () => {
    const mappings = sequentialBookChapterMappings(
      [
        readable("title", "Title page"),
        readable("prologue", "Prologue"),
        readable("chapter-1", "Chapter 1"),
        readable("chapter-2", "Chapter 2"),
      ],
      [
        audio("audio-1", null, "File 1", 0, 600),
        audio("audio-2", "marker-1", "Opening", 0, 12.5),
        audio("audio-2", "marker-2", "Chapter One", 12.5, 180),
      ],
      "prologue",
    );

    // Filled rows remember they were filled in order; they are never saved as hand-picked pairs.
    const ordered = BOOK_CHAPTER_MAPPING_ORIGIN.ordered;
    expect(mappings).toEqual([
      { readableChapterKey: "prologue", audioTrackId: "audio-1", origin: ordered },
      { readableChapterKey: "chapter-1", audioTrackId: "audio-2", audioMarkerId: "marker-1", origin: ordered },
      { readableChapterKey: "chapter-2", audioTrackId: "audio-2", audioMarkerId: "marker-2", origin: ordered },
    ]);
  });

  it("reports two progresses only for a Separate book that has both formats", () => {
    const alignment = (
      state: BookLinkStateCode,
      modalities = [CONSUMPTION_MODALITY.reading, CONSUMPTION_MODALITY.listening],
    ) => ({
      modalities,
      readablePositionTotal: 10_000,
      rows: [],
      coverage: {} as BookAlignmentResponse["coverage"],
      resume: null,
      link: {
        state,
        reason: ALIGNMENT_GAP_REASON.audioInParts,
        audioStructure: AUDIOBOOK_STRUCTURE.parts,
        readingPercent: 0.254,
        listeningPercent: "0.36",
      },
    }) as BookAlignmentResponse;

    expect(bookSeparateProgress(alignment(BOOK_LINK_STATE.separate))).toEqual({
      readingPercent: 25,
      listeningPercent: 36,
      reason: "This audiobook is split into parts rather than chapters, so reading and listening are tracked separately.",
    });
    expect(bookSeparateProgress(alignment(BOOK_LINK_STATE.linked))).toBeNull();
    expect(bookSeparateProgress(alignment(BOOK_LINK_STATE.separate, [CONSUMPTION_MODALITY.listening]))).toBeNull();
    expect(bookSeparateProgress(null)).toBeNull();
  });
});
