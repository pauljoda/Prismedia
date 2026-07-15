import { describe, expect, it } from "vitest";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import {
  buildBookChapterRows,
  chapterMatchKey,
  type ReadableBookChapter,
} from "./book-chapter-list";

function audioTrack(id: string, title: string, sortOrder: number): AudioTrackListItemDto {
  return {
    id,
    title,
    date: null,
    rating: null,
    organized: false,
    isNsfw: false,
    duration: 600,
    bitRate: null,
    sampleRate: null,
    channels: null,
    codec: null,
    fileSize: null,
    embeddedArtist: null,
    embeddedAlbum: null,
    trackNumber: sortOrder,
    sectionLabel: null,
    sectionKey: null,
    waveformPath: null,
    libraryId: "book-1",
    sortOrder,
    studioId: null,
    performers: [],
    tags: [],
    playCount: 0,
    lastPlayedAt: null,
    createdAt: "",
  };
}

function readable(id: string, title: string, order: number): ReadableBookChapter {
  return {
    id,
    title,
    order,
    depth: 0,
    target: { kind: "epub", location: `Text/${id}.xhtml` },
  };
}

describe("book chapter list", () => {
  it("normalizes chapter labels without erasing their meaningful titles", () => {
    expect(chapterMatchKey("Chapter 01 — The Boy Who Lived")).toBe("the boy who lived");
    expect(chapterMatchKey("01. The Boy Who Lived")).toBe("the boy who lived");
    expect(chapterMatchKey("Prologue")).toBe("prologue");
  });

  it("matches audio parts to readable chapters by normalized title before position", () => {
    const rows = buildBookChapterRows({
      readableChapters: [
        readable("chapter-1", "Chapter 1: Bran", 0),
        readable("chapter-2", "Chapter 2: Catelyn", 1),
      ],
      audioTracks: [
        audioTrack("audio-2", "02 - Catelyn", 1),
        audioTrack("audio-1", "01 - Bran", 0),
      ],
      currentReadableId: "chapter-2",
      currentAudioTrackId: "audio-1",
    });

    expect(rows.map((row) => [row.title, row.audioTrack?.id])).toEqual([
      ["Chapter 1: Bran", "audio-1"],
      ["Chapter 2: Catelyn", "audio-2"],
    ]);
    expect(rows[0]).toMatchObject({ isCurrentAudio: true, isCurrentReading: false });
    expect(rows[1]).toMatchObject({ isCurrentAudio: false, isCurrentReading: true });
  });

  it("uses chapter numbers when text and audio titles differ", () => {
    const rows = buildBookChapterRows({
      readableChapters: [
        readable("chapter-1", "Chapter 1: An Unexpected Party", 0),
        readable("chapter-2", "Chapter 2: Roast Mutton", 1),
      ],
      audioTracks: [
        audioTrack("audio-1", "Track 1", 0),
        audioTrack("audio-2", "Track 2", 1),
      ],
    });

    expect(rows.map((row) => row.audioTrack?.id)).toEqual(["audio-1", "audio-2"]);
  });

  it("keeps unmatched audio visible instead of attaching it to the wrong chapter", () => {
    const rows = buildBookChapterRows({
      readableChapters: [readable("prologue", "Prologue", 0), readable("chapter-1", "Bran", 1)],
      audioTracks: [
        audioTrack("credits", "Publisher credits", 0),
        audioTrack("appendix", "Historical appendix", 1),
        audioTrack("interview", "Author interview", 2),
      ],
    });

    expect(rows).toHaveLength(5);
    expect(rows.slice(0, 2).every((row) => row.audioTrack == null)).toBe(true);
    expect(rows.slice(2).map((row) => row.audioTrack?.id)).toEqual([
      "credits",
      "appendix",
      "interview",
    ]);
  });

  it("pairs remaining rows by order only when the counts agree", () => {
    const rows = buildBookChapterRows({
      readableChapters: [readable("one", "First movement", 0), readable("two", "Second movement", 1)],
      audioTracks: [audioTrack("audio-a", "Part A", 0), audioTrack("audio-b", "Part B", 1)],
    });

    expect(rows.map((row) => row.audioTrack?.id)).toEqual(["audio-a", "audio-b"]);
  });
});
