import { describe, expect, it } from "vitest";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import {
  buildBookChapterRows,
  sequentialBookChapterMappings,
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
    accessCount: 0,
    lastActiveAt: null,
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
  it("applies the persisted chapter map regardless of origin", () => {
    const rows = buildBookChapterRows({
      readableChapters: [
        readable("chapter-1", "Chapter 1: Bran", 0),
        readable("chapter-2", "Chapter 2: Catelyn", 1),
      ],
      audioTracks: [
        audioTrack("audio-2", "02 - Catelyn", 1),
        audioTrack("audio-1", "01 - Bran", 0),
      ],
      chapterMappings: [
        { readableChapterKey: "chapter-1", audioTrackId: "audio-1", origin: "auto" },
        { readableChapterKey: "chapter-2", audioTrackId: "audio-2", origin: "manual" },
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

  it("never matches by title in the client — unmapped tracks stay unattached", () => {
    // Matching is computed and persisted server-side; identical titles alone must not attach.
    const rows = buildBookChapterRows({
      readableChapters: [readable("prologue", "Prologue", 0)],
      audioTracks: [audioTrack("audio-1", "Prologue", 0)],
    });

    expect(rows[0]?.audioTrack).toBeNull();
    expect(rows[1]?.audioTrack?.id).toBe("audio-1");
  });

  it("ignores mappings whose chapter or track is not present", () => {
    const rows = buildBookChapterRows({
      readableChapters: [readable("chapter-1", "Bran", 0)],
      audioTracks: [audioTrack("audio-1", "Bran", 0)],
      chapterMappings: [
        { readableChapterKey: "vanished-chapter", audioTrackId: "audio-1" },
        { readableChapterKey: "chapter-1", audioTrackId: "vanished-track" },
      ],
    });

    expect(rows[0]?.audioTrack).toBeNull();
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

  it("maps ordered audio files sequentially from the chapter the user marks first", () => {
    const mappings = sequentialBookChapterMappings(
      [
        readable("title", "Title page", 0),
        readable("prologue", "Prologue", 1),
        readable("chapter-1", "Chapter 1", 2),
        readable("chapter-2", "Chapter 2", 3),
      ],
      [
        audioTrack("audio-2", "File 2", 1),
        audioTrack("audio-1", "File 1", 0),
        audioTrack("audio-3", "File 3", 2),
      ],
      "prologue",
    );

    expect(mappings).toEqual([
      { readableChapterKey: "prologue", audioTrackId: "audio-1" },
      { readableChapterKey: "chapter-1", audioTrackId: "audio-2" },
      { readableChapterKey: "chapter-2", audioTrackId: "audio-3" },
    ]);
  });
});
