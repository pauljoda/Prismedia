import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import TrackListRow from "./TrackListRow.svelte";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";

describe("TrackListRow", () => {
  it("displays zero-based structural track order as one-based numbers", () => {
    const { container } = render(TrackListRow, {
      props: {
        track: { ...track("track-1", "Prelude in E minor"), trackNumber: 0 },
        index: 0,
        isActive: false,
        isPlaying: false,
        onPlay: vi.fn(),
      },
    });

    expect(container.querySelector(".index-cell span")?.textContent?.trim()).toBe("1");
  });

  it("updates the row rating without starting playback", async () => {
    const onPlay = vi.fn();
    const onRatingChange = vi.fn();

    render(TrackListRow, {
      props: {
        track: track("track-1", "Prelude in E minor"),
        index: 0,
        isActive: false,
        isPlaying: false,
        onPlay,
        onRatingChange,
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Rate Prelude in E minor with 4 star rating" }));

    expect(onRatingChange).toHaveBeenCalledWith("track-1", 4);
    expect(onPlay).not.toHaveBeenCalled();
  });

  it("keeps unrated row rating controls visible", () => {
    render(TrackListRow, {
      props: {
        track: track("track-1", "Prelude in E minor"),
        index: 0,
        isActive: false,
        isPlaying: false,
        onPlay: vi.fn(),
        onRatingChange: vi.fn(),
      },
    });

    const ratingButton = screen.getByRole("button", { name: "Rate Prelude in E minor with 1 star rating" });
    expect(ratingButton.parentElement?.parentElement).not.toHaveClass("opacity-0");
  });

  it("selects a row without starting playback", async () => {
    const onPlay = vi.fn();
    const onSelectedChange = vi.fn();

    render(TrackListRow, {
      props: {
        track: track("track-1", "Prelude in E minor"),
        index: 0,
        isActive: false,
        isPlaying: false,
        onPlay,
        selectable: true,
        selected: false,
        onSelectedChange,
      },
    });

    await fireEvent.click(screen.getByLabelText("Select Prelude in E minor"));

    expect(onSelectedChange).toHaveBeenCalledWith(true);
    expect(onPlay).not.toHaveBeenCalled();
  });

  it("shows a wanted track as missing and prevents playback or selection", async () => {
    const onPlay = vi.fn();
    const onSelectedChange = vi.fn();
    const missing = { ...track("track-missing", "Happy"), isWanted: true, hasSourceMedia: false };

    const { container } = render(TrackListRow, {
      props: {
        track: missing,
        index: 3,
        isActive: false,
        isPlaying: false,
        onPlay,
        selectable: true,
        selected: false,
        onSelectedChange,
      },
    });

    expect(screen.getByText("Missing · not playable")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Play Happy" })).not.toBeInTheDocument();
    expect(screen.getByLabelText("Select Happy")).toBeDisabled();
    await fireEvent.click(container.querySelector(".track-row")!);
    expect(onPlay).not.toHaveBeenCalled();
    expect(onSelectedChange).not.toHaveBeenCalled();
  });

  it("opens a row actions flyout and renames a track without starting playback", async () => {
    const onPlay = vi.fn();
    const onRename = vi.fn().mockResolvedValue(undefined);

    render(TrackListRow, {
      props: {
        track: track("track-1", "Prelude in E minor"),
        index: 0,
        isActive: false,
        isPlaying: false,
        onPlay,
        onRename,
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Track actions for Prelude in E minor" }));
    await fireEvent.click(screen.getByRole("menuitem", { name: "Rename" }));

    const input = screen.getByLabelText("Track title");
    await fireEvent.input(input, { target: { value: "Prelude, Op. 28 No. 4" } });
    await fireEvent.click(screen.getByRole("button", { name: "Save track title" }));

    await waitFor(() => {
      expect(onRename).toHaveBeenCalledWith(track("track-1", "Prelude in E minor"), "Prelude, Op. 28 No. 4");
    });
    expect(onPlay).not.toHaveBeenCalled();
  });

});

function track(id: string, title: string): AudioTrackListItemDto {
  return {
    id,
    title,
    date: null,
    rating: null,
    organized: false,
    isNsfw: false,
    duration: 93,
    bitRate: null,
    sampleRate: null,
    channels: null,
    codec: null,
    fileSize: null,
    embeddedArtist: "Musopen",
    embeddedAlbum: "The Complete Chopin Collection",
    trackNumber: 1,
    sectionLabel: null,
    waveformPath: null,
    libraryId: "library-1",
    sortOrder: 1,
    studioId: null,
    performers: [],
    tags: [],
    playCount: 0,
    lastPlayedAt: null,
    createdAt: "",
  };
}
