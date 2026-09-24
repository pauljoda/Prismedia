import { describe, expect, it } from "vitest";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import type { BookAudioChapter } from "$lib/api/generated/model";
import {
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

function audioChapter(
  markerId: string,
  title: string,
  startSeconds: number,
  endSeconds: number,
): BookAudioChapter {
  return {
    audioTrackId: "audio-1",
    audioMarkerId: markerId,
    title,
    startSeconds,
    endSeconds,
  };
}

describe("book chapter list", () => {
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

  it("creates sequential overrides for embedded chapter candidates", () => {
    const mappings = sequentialBookChapterMappings(
      [readable("opening", "Opening Credits", 0), readable("chapter-1", "Chapter One", 1)],
      [audioTrack("audio-1", "Whole Book", 0)],
      "opening",
      [
        audioChapter("marker-1", "Opening Credits", 0, 12.5),
        audioChapter("marker-2", "Chapter One", 12.5, 180),
      ],
    );

    expect(mappings).toEqual([
      { readableChapterKey: "opening", audioTrackId: "audio-1", audioMarkerId: "marker-1" },
      { readableChapterKey: "chapter-1", audioTrackId: "audio-1", audioMarkerId: "marker-2" },
    ]);
  });
});
