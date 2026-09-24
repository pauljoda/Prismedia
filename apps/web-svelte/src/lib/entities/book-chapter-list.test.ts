import { describe, expect, it } from "vitest";
import type { AudioChapterWindow, ReadableChapterWindow } from "$lib/api/generated/model";
import { audioWindowKey, sequentialBookChapterMappings } from "./book-chapter-list";

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

    expect(mappings).toEqual([
      { readableChapterKey: "prologue", audioTrackId: "audio-1" },
      { readableChapterKey: "chapter-1", audioTrackId: "audio-2", audioMarkerId: "marker-1" },
      { readableChapterKey: "chapter-2", audioTrackId: "audio-2", audioMarkerId: "marker-2" },
    ]);
  });
});
