import { render, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AudioVidStackPlayer from "./AudioVidStackPlayer.svelte";
import type { AudioTrackListItemDto } from "@prismedia/contracts";

const tracks: AudioTrackListItemDto[] = [
  track("track-1", "First", 10),
  track("track-2", "Second", 20),
];

describe("AudioVidStackPlayer", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response("", { status: 404 })),
    );
    Object.defineProperty(HTMLMediaElement.prototype, "play", {
      configurable: true,
      value: vi.fn().mockResolvedValue(undefined),
    });
  });

  it("resets the native seek position when switching tracks", async () => {
    const { container, rerender } = render(AudioVidStackPlayer, {
      props: {
        tracks,
        activeTrackId: "track-1",
        onTrackChange: vi.fn(),
      },
    });
    const audio = container.querySelector("audio") as HTMLAudioElement;
    audio.currentTime = 10;

    await rerender({
      tracks,
      activeTrackId: "track-2",
      onTrackChange: vi.fn(),
    });

    await waitFor(() => {
      expect(audio.currentTime).toBe(0);
    });
  });
});

function track(id: string, title: string, duration: number): AudioTrackListItemDto {
  return {
    id,
    title,
    date: null,
    rating: null,
    organized: false,
    isNsfw: false,
    duration,
    bitRate: null,
    sampleRate: null,
    channels: null,
    codec: "aac",
    fileSize: null,
    embeddedArtist: null,
    embeddedAlbum: null,
    trackNumber: null,
    waveformPath: null,
    libraryId: "library-1",
    sortOrder: 0,
    studioId: null,
    performers: [],
    tags: [],
    playCount: 0,
    lastPlayedAt: null,
    createdAt: "",
  };
}
