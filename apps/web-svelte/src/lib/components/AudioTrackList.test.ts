import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import AudioTrackList from "./AudioTrackList.svelte";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";

describe("AudioTrackList", () => {
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
    await fireEvent.click(screen.getByRole("button", { name: "Queue Next" }));

    expect(onBulk).toHaveBeenCalledWith(["track-1", "track-2"]);
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
