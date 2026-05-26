import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import type { AudioTrackListItemDto } from "@prismedia/contracts";
import TrackListRow from "./TrackListRow.svelte";

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
