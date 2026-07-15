import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { BookChapterRow } from "$lib/entities/book-chapter-list";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import BookChapterList from "./BookChapterList.svelte";

function track(): AudioTrackListItemDto {
  return {
    id: "audio-1",
    title: "Chapter One",
    date: null,
    rating: null,
    organized: false,
    isNsfw: false,
    duration: 900,
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

const row: BookChapterRow = {
  id: "chapter-1",
  title: "Chapter One",
  order: 0,
  depth: 0,
  readTarget: { kind: "epub", location: "Text/chapter-1.xhtml" },
  audioTrack: track(),
  isCurrentReading: true,
  isCurrentAudio: true,
};

describe("BookChapterList", () => {
  afterEach(cleanup);

  it("offers read, listen, and combined actions for a matched chapter", async () => {
    const onRead = vi.fn();
    const onListen = vi.fn();
    const onCombined = vi.fn();
    render(BookChapterList, { rows: [row], onRead, onListen, onCombined });

    await fireEvent.click(screen.getByRole("button", { name: "Read Chapter One" }));
    await fireEvent.click(screen.getByRole("button", { name: "Listen to Chapter One" }));
    await fireEvent.click(screen.getByRole("button", { name: "Read and listen to Chapter One" }));

    expect(onRead).toHaveBeenCalledWith(row);
    expect(onListen).toHaveBeenCalledWith(row);
    expect(onCombined).toHaveBeenCalledWith(row);
  });

  it("shows both independent current-position labels on one row", () => {
    render(BookChapterList, {
      rows: [row],
      readingProgressLabel: "42% of book",
      listeningProgressLabel: "12:10 into audiobook",
      onRead: vi.fn(),
      onListen: vi.fn(),
      onCombined: vi.fn(),
    });

    expect(screen.getByText("Reading · 42% of book")).toBeInTheDocument();
    expect(screen.getByText("Listening · 12:10 into audiobook")).toBeInTheDocument();
    expect(screen.getByTestId("reading-rail-chapter-1")).toBeInTheDocument();
    expect(screen.getByTestId("listening-rail-chapter-1")).toBeInTheDocument();
  });

  it("does not show actions that a row cannot perform", () => {
    render(BookChapterList, {
      rows: [{ ...row, readTarget: null, isCurrentReading: false }],
      onRead: vi.fn(),
      onListen: vi.fn(),
      onCombined: vi.fn(),
    });

    expect(screen.queryByRole("button", { name: "Read Chapter One" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Read and listen to Chapter One" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Listen to Chapter One" })).toBeInTheDocument();
  });

  it("offers combined mode for a matched page-based chapter", () => {
    render(BookChapterList, {
      rows: [{
        ...row,
        readTarget: { kind: "entity-chapter", chapterId: "chapter-1" },
        readPageCount: 20,
      }],
      onRead: vi.fn(),
      onListen: vi.fn(),
      onCombined: vi.fn(),
    });

    expect(screen.getByRole("button", { name: "Read and listen to Chapter One" })).toBeInTheDocument();
  });
});
