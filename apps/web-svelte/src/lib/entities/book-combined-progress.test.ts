import { describe, expect, it } from "vitest";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import type { BookChapterRow } from "$lib/entities/book-chapter-list";
import {
  resolveBookCombinedResume,
  resolveChapterCombinedLaunch,
  type BookListeningPosition,
  type BookReadingPosition,
} from "./book-combined-progress";

function audioTrack(duration = 1_200): AudioTrackListItemDto {
  return {
    id: "audio-1",
    title: "Chapter One",
    date: null,
    rating: null,
    organized: false,
    isNsfw: false,
    duration,
    bitRate: null,
    sampleRate: null,
    channels: null,
    codec: null,
    fileSize: null,
    embeddedArtist: null,
    embeddedAlbum: null,
    trackNumber: 1,
    sectionLabel: null,
    sectionKey: null,
    waveformPath: null,
    libraryId: "book-1",
    sortOrder: 0,
    studioId: null,
    performers: [],
    tags: [],
    playCount: 0,
    lastPlayedAt: null,
    createdAt: "",
  };
}

function epubRow(): BookChapterRow {
  return {
    id: "chapter-1",
    title: "Chapter One",
    order: 0,
    depth: 0,
    readTarget: {
      kind: "epub",
      location: "Text/chapter-1.xhtml",
      startFraction: 0.2,
      endFraction: 0.4,
    },
    readPageCount: null,
    audioTrack: audioTrack(),
    isCurrentReading: true,
    isCurrentAudio: true,
  };
}

const reading: BookReadingPosition = {
  rowId: "chapter-1",
  overallFraction: 0.3,
  chapterFraction: 0.5,
  location: "epubcfi(/6/8!/4/2)",
  pageIndex: null,
};

const listening: BookListeningPosition = {
  rowId: "chapter-1",
  overallFraction: 0.24,
  chapterFraction: 0.2,
  trackOffsetSeconds: 240,
};

describe("combined book progress", () => {
  it("interpolates a reading cursor into audio and gives it a five-second runway", () => {
    expect(resolveChapterCombinedLaunch(epubRow(), reading, listening)).toEqual({
      rowId: "chapter-1",
      source: "reading",
      audioStartSeconds: 595,
      readerLocation: "epubcfi(/6/8!/4/2)",
      readerFraction: null,
      readerPageIndex: null,
    });
  });

  it("starts from zero when the interpolated audio point is inside the runway", () => {
    expect(resolveChapterCombinedLaunch(epubRow(), {
      ...reading,
      overallFraction: 0.201,
      chapterFraction: 0.002,
    }, null)?.audioStartSeconds).toBe(0);
  });

  it("uses listening when it is farther and maps its chapter fraction back into the EPUB", () => {
    expect(resolveChapterCombinedLaunch(epubRow(), reading, {
      ...listening,
      overallFraction: 0.36,
      chapterFraction: 0.8,
      trackOffsetSeconds: 960,
    })).toEqual({
      rowId: "chapter-1",
      source: "listening",
      audioStartSeconds: 955,
      readerLocation: null,
      readerFraction: 0.36,
      readerPageIndex: null,
    });
  });

  it("maps listening progress to a page for archive chapters", () => {
    const row: BookChapterRow = {
      ...epubRow(),
      readTarget: { kind: "entity-chapter", chapterId: "chapter-1" },
      readPageCount: 20,
    };

    expect(resolveChapterCombinedLaunch(row, null, {
      ...listening,
      chapterFraction: 0.5,
      trackOffsetSeconds: 600,
    })?.readerPageIndex).toBe(9);
  });

  it("chooses the row owned by whichever whole-book cursor is farther", () => {
    const secondRow: BookChapterRow = {
      ...epubRow(),
      id: "chapter-2",
      readTarget: {
        kind: "epub",
        location: "Text/chapter-2.xhtml",
        startFraction: 0.4,
        endFraction: 0.6,
      },
      audioTrack: { ...audioTrack(), id: "audio-2" },
    };

    const plan = resolveBookCombinedResume([epubRow(), secondRow], reading, {
      ...listening,
      rowId: "chapter-2",
      overallFraction: 0.55,
      chapterFraction: 0.75,
      trackOffsetSeconds: 900,
    });

    expect(plan).toMatchObject({ rowId: "chapter-2", source: "listening" });
  });
});
