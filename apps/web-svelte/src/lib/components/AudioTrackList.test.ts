import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import AudioTrackList from "./AudioTrackList.svelte";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";

describe("AudioTrackList", () => {
  it("keeps disc grouping for albums but uses continuous numbering for library results", async () => {
    const tracks = [
      { ...track("track-1", "Prelude"), sectionLabel: "Disc one", trackNumber: 7 },
      { ...track("track-2", "Nocturne"), sectionLabel: "Disc two", trackNumber: 3 },
    ];
    const { container, rerender } = render(AudioTrackList, {
      tracks,
      activeTrackId: null,
      isPlaying: false,
      onPlay: vi.fn(),
    });

    expect(screen.getByText("Disc one")).toBeInTheDocument();
    expect(screen.getByText("Disc two")).toBeInTheDocument();
    const numbers = () => Array.from(container.querySelectorAll(".index-cell > span:first-child"))
      .map((node) => node.textContent?.trim());
    expect(numbers()).toEqual(["1", "1"]);

    await rerender({ groupBySection: false });
    expect(screen.queryByText("Disc one")).not.toBeInTheDocument();
    expect(screen.queryByText("Disc two")).not.toBeInTheDocument();
    expect(numbers()).toEqual(["1", "2"]);
    expect(screen.getAllByRole("link").map((link) => link.textContent?.trim()))
      .toEqual(["Prelude", "Nocturne"]);
  });

  it("selects all tracks and exposes collection plus bulk actions", async () => {
    const onBulk = vi.fn();

    render(AudioTrackList, {
      props: {
        tracks: [
          track("track-1", "Prelude in E minor"),
          track("track-2", "Nocturne in C minor"),
        ],
        activeTrackId: null,
        isPlaying: false,
        onPlay: vi.fn(),
        bulkActions: [
          {
            id: "queue-next",
            label: "Queue Next",
            onRun: onBulk,
          },
        ],
      },
    });

    await fireEvent.click(screen.getByLabelText("Select all tracks"));

    expect(screen.getAllByText("2 selected")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Add selection to a collection" })).toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Bulk actions" }));
    await fireEvent.click(screen.getByRole("menuitem", { name: "Queue Next" }));

    expect(onBulk).toHaveBeenCalledWith(["track-1", "track-2"]);
  });

  it("keeps missing tracks visible while select-all targets only present tracks", async () => {
    const onSelectionChange = vi.fn();
    render(AudioTrackList, {
      props: {
        tracks: [
          { ...track("track-present", "Happy"), isWanted: false, hasSourceMedia: true },
          { ...track("track-missing", "Scream"), isWanted: true, hasSourceMedia: false },
        ],
        activeTrackId: null,
        isPlaying: false,
        onPlay: vi.fn(),
        onSelectionChange,
      },
    });

    expect(screen.getByText("1 present · 1 missing")).toBeInTheDocument();
    expect(screen.getByText("Missing · not playable")).toBeInTheDocument();
    await fireEvent.click(screen.getByLabelText("Select all tracks"));
    expect(onSelectionChange).toHaveBeenLastCalledWith(["track-present"]);
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
    accessCount: 0,
    lastActiveAt: null,
    createdAt: "",
  };
}
