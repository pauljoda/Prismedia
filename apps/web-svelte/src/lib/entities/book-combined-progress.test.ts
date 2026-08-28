import { describe, expect, it } from "vitest";
import { PROGRESS_UNIT, READER_MODE } from "$lib/api/generated/codes";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import type { BookChapterRow } from "$lib/entities/book-chapter-list";
import {
  buildBookProgressMappings,
  bookProgressUpdateForAudio,
  legacyBookProgressPromotion,
  resolveBookAudioResume,
  resolveBookCombinedResume,
  resolveBookProgressMapping,
  resolveChapterCombinedLaunch,
  shouldPromoteLegacyBookProgress,
  type BookReadingPosition,
} from "./book-combined-progress";

function audioTrack(id = "audio-1", duration = 1_200): AudioTrackListItemDto {
  return {
    id,
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
    accessCount: 0,
    lastActiveAt: null,
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

describe("unified book progress", () => {
  it("uses the one canonical reading cursor to align audio with a five-second runway", () => {
    expect(resolveChapterCombinedLaunch(epubRow(), reading)).toEqual({
      rowId: "chapter-1",
      source: "progress",
      audioStartSeconds: 595,
      readerLocation: "epubcfi(/6/8!/4/2)",
      readerFraction: null,
      readerPageIndex: null,
    });
  });

  it("falls back to the saved text fraction when an estimated audio cursor has no exact CFI", () => {
    expect(resolveChapterCombinedLaunch(epubRow(), {
      ...reading,
      location: null,
    })).toMatchObject({
      audioStartSeconds: 595,
      readerLocation: null,
      readerFraction: 0.3,
    });
  });

  it("falls back to the mapped fraction when native saved progress produced the latest cursor", () => {
    expect(resolveChapterCombinedLaunch(epubRow(), {
      ...reading,
      location: '{"href":"Text/chapter-1.xhtml","locations":{"progression":0.5}}',
    })).toMatchObject({
      audioStartSeconds: 595,
      readerLocation: null,
      readerFraction: 0.3,
    });
  });

  it("resumes the row owned by the one whole-book cursor", () => {
    const secondRow: BookChapterRow = {
      ...epubRow(),
      id: "chapter-2",
      readTarget: {
        kind: "epub",
        location: "Text/chapter-2.xhtml",
        startFraction: 0.4,
        endFraction: 0.6,
      },
      audioTrack: audioTrack("audio-2"),
    };

    expect(resolveBookCombinedResume([epubRow(), secondRow], {
      ...reading,
      rowId: "chapter-2",
      overallFraction: 0.55,
      chapterFraction: 0.75,
      location: null,
    })).toMatchObject({ rowId: "chapter-2", source: "progress" });
  });

  it("builds chapter-scoped EPUB mappings and converts listening into the shared CFI fraction", () => {
    const mapping = expectSingle(buildBookProgressMappings(
      "book-1",
      [epubRow()],
      READER_MODE.paged,
    ));

    expect(mapping).toEqual({
      itemId: "audio-1",
      currentEntityId: "book-1",
      unit: PROGRESS_UNIT.cfi,
      startIndex: 2000,
      endIndex: 4000,
      total: 10000,
      mode: READER_MODE.paged,
    });
    expect(bookProgressUpdateForAudio(mapping, 900, 1200, 15, false)).toEqual({
      currentEntityId: "book-1",
      unit: PROGRESS_UNIT.cfi,
      index: 3500,
      total: 10000,
      mode: READER_MODE.paged,
      location: null,
      completed: null,
      activitySeconds: 15,
      activityKind: "listening",
    });
  });

  it("maps audio to a page only within the matched readable chapter", () => {
    const row: BookChapterRow = {
      ...epubRow(),
      readTarget: { kind: "entity-chapter", chapterId: "chapter-entity-1" },
      readPageCount: 20,
    };
    const mapping = expectSingle(buildBookProgressMappings(
      "book-1",
      [row],
      READER_MODE.webtoon,
    ));

    expect(bookProgressUpdateForAudio(mapping, 600, 1200, null, false)).toMatchObject({
      currentEntityId: "chapter-entity-1",
      unit: PROGRESS_UNIT.page,
      index: 9,
      total: 20,
      mode: READER_MODE.webtoon,
    });
  });

  it("does not let an unmatched audio part invent a text position", () => {
    const unmatched = { ...epubRow(), readTarget: null };
    expect(buildBookProgressMappings("book-1", [epubRow(), unmatched], READER_MODE.paged))
      .toHaveLength(1);
  });

  it("uses second progress only when the book has no readable rendition", () => {
    const audioOnly = { ...epubRow(), readTarget: null };
    expect(expectSingle(buildBookProgressMappings("book-1", [audioOnly], READER_MODE.paged)))
      .toEqual({
        itemId: "audio-1",
        currentEntityId: "book-1",
        unit: PROGRESS_UNIT.second,
        startIndex: 0,
        endIndex: 1200,
        total: 1200,
        mode: null,
      });
  });

  it("maps the shared cursor back into the audiobook part for listening resume", () => {
    const rows = [epubRow()];
    const mappings = buildBookProgressMappings("book-1", rows, READER_MODE.paged);

    expect(resolveBookAudioResume(rows, mappings, {
      currentEntityId: "book-1",
      unit: PROGRESS_UNIT.cfi,
      index: 3500,
    })).toEqual({ trackId: "audio-1", trackOffsetSeconds: 895 });
  });

  it("assigns a shared rounded boundary to the later chapter", () => {
    const secondRow: BookChapterRow = {
      ...epubRow(),
      id: "chapter-2",
      readTarget: {
        kind: "epub",
        location: "Text/chapter-2.xhtml",
        startFraction: 0.4,
        endFraction: 0.6,
      },
      audioTrack: audioTrack("audio-2"),
    };
    const mappings = buildBookProgressMappings("book-1", [epubRow(), secondRow], READER_MODE.paged);

    expect(resolveBookProgressMapping(mappings, {
      currentEntityId: "book-1",
      unit: PROGRESS_UNIT.cfi,
      index: 4000,
    })?.itemId).toBe("audio-2");
  });

  it("does not invent an audiobook chapter for a readable cursor outside mapped ranges", () => {
    const mappings = buildBookProgressMappings("book-1", [epubRow()], READER_MODE.paged);

    expect(resolveBookProgressMapping(mappings, {
      currentEntityId: "book-1",
      unit: PROGRESS_UNIT.cfi,
      index: 9000,
    })).toBeNull();
  });

  it("promotes a farther legacy audiobook resume into canonical progress", () => {
    const secondRow: BookChapterRow = {
      ...epubRow(),
      id: "chapter-2",
      readTarget: {
        kind: "epub",
        location: "Text/chapter-2.xhtml",
        startFraction: 0.4,
        endFraction: 0.6,
      },
      audioTrack: { ...audioTrack("audio-2"), sortOrder: 1 },
    };
    const rows = [epubRow(), secondRow];
    const mappings = buildBookProgressMappings("book-1", rows, READER_MODE.paged);
    const promotion = legacyBookProgressPromotion(rows, mappings, 2_100);

    expect(promotion).toMatchObject({
      mapping: { itemId: "audio-2" },
      update: { index: 5500, location: null, activitySeconds: null },
    });
    expect(shouldPromoteLegacyBookProgress(mappings, {
      currentEntityId: "book-1",
      unit: PROGRESS_UNIT.cfi,
      index: 3500,
    }, promotion)).toBe(true);
  });

  it("preserves a farther or unresolvable readable cursor during legacy promotion", () => {
    const rows = [epubRow()];
    const mappings = buildBookProgressMappings("book-1", rows, READER_MODE.paged);
    const promotion = legacyBookProgressPromotion(rows, mappings, 600);

    expect(shouldPromoteLegacyBookProgress(mappings, {
      currentEntityId: "book-1",
      unit: PROGRESS_UNIT.cfi,
      index: 3900,
    }, promotion)).toBe(false);
    expect(shouldPromoteLegacyBookProgress(mappings, {
      currentEntityId: "unmatched-readable-chapter",
      unit: PROGRESS_UNIT.page,
      index: 10,
    }, promotion)).toBe(false);
  });

  it("uses an embedded M4B chapter window for launch, progress, and resume", () => {
    const row: BookChapterRow = {
      ...epubRow(),
      audioMarkerId: "marker-2",
      audioStartSeconds: 100,
      audioEndSeconds: 200,
    };
    const launch = resolveChapterCombinedLaunch(row, reading);
    const mapping = expectSingle(buildBookProgressMappings("book-1", [row], READER_MODE.paged));

    expect(launch?.audioStartSeconds).toBe(145);
    expect(mapping).toMatchObject({
      itemId: "audio-1",
      sourceStartSeconds: 100,
      sourceEndSeconds: 200,
    });
    expect(bookProgressUpdateForAudio(mapping, 150, 1200, null, false).index).toBe(3000);
    expect(resolveBookAudioResume([row], [mapping], {
      currentEntityId: "book-1",
      unit: PROGRESS_UNIT.cfi,
      index: 3500,
    })).toEqual({ trackId: "audio-1", trackOffsetSeconds: 170 });
    expect(legacyBookProgressPromotion([row], [mapping], 150)).toMatchObject({
      mapping: { sourceStartSeconds: 100, sourceEndSeconds: 200 },
      update: { index: 3000 },
    });
  });
});

function expectSingle<T>(items: readonly T[]): T {
  expect(items).toHaveLength(1);
  return items[0]!;
}
