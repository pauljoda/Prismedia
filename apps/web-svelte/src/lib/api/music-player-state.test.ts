import { beforeEach, describe, expect, it, vi } from "vitest";

const getMusicPlayerState = vi.hoisted(() => vi.fn());
const updateMusicPlayerState = vi.hoisted(() => vi.fn());
const clearMusicPlayerState = vi.hoisted(() => vi.fn());

vi.mock("$lib/api/generated/prismedia", () => ({
  getMusicPlayerState,
  updateMusicPlayerState,
  clearMusicPlayerState,
}));

vi.mock("$lib/entities/audio-track-items", () => ({
  audioTrackDetailToListItem: (track: { id: string; title: string }) => ({
    id: track.id,
    title: track.title,
    duration: 120,
  }),
}));

import { fetchMusicPlayerState, saveMusicPlayerState } from "./music-player-state";
import { MUSIC_PLAYER_MINI_SIDE, MUSIC_PLAYER_REPEAT_MODE } from "$lib/api/generated/codes";

describe("music player state API", () => {
  beforeEach(() => {
    getMusicPlayerState.mockReset();
    updateMusicPlayerState.mockReset().mockResolvedValue({ data: null });
    clearMusicPlayerState.mockReset().mockResolvedValue({ data: null });
  });

  it("maps currentTime from the browser-scoped player response", async () => {
    getMusicPlayerState.mockResolvedValue({
      data: {
        tracks: [{ id: "track-1", title: "Track 1" }],
        order: [0],
        position: 0,
        currentTime: 42,
        playing: true,
        shuffle: false,
        repeat: MUSIC_PLAYER_REPEAT_MODE.off,
        volume: 0.7,
        muted: false,
        collapsed: false,
        collapsedSide: MUSIC_PLAYER_MINI_SIDE.left,
        context: null,
      },
    });

    const state = await fetchMusicPlayerState();

    expect(state.currentTime).toBe(42);
    expect(state.queue[0].id).toBe("track-1");
  });

  it("sends currentTime when saving a non-empty queue", async () => {
    await saveMusicPlayerState({
      queueTrackIds: ["track-1"],
      order: [0],
      position: 0,
      currentTime: 17,
      playing: false,
      shuffle: false,
      repeat: MUSIC_PLAYER_REPEAT_MODE.off,
      volume: 0.4,
      muted: false,
      collapsed: false,
      collapsedSide: MUSIC_PLAYER_MINI_SIDE.left,
      context: null,
    });

    expect(updateMusicPlayerState).toHaveBeenCalledWith(expect.objectContaining({ currentTime: 17 }));
    expect(clearMusicPlayerState).not.toHaveBeenCalled();
  });

  it("clears the playback queue document for an empty queue", async () => {
    await saveMusicPlayerState({
      queueTrackIds: [],
      order: [],
      position: -1,
      currentTime: 0,
      playing: false,
      shuffle: false,
      repeat: MUSIC_PLAYER_REPEAT_MODE.off,
      volume: 0.4,
      muted: false,
      collapsed: false,
      collapsedSide: MUSIC_PLAYER_MINI_SIDE.left,
      context: null,
    });

    expect(clearMusicPlayerState).toHaveBeenCalled();
    expect(updateMusicPlayerState).not.toHaveBeenCalled();
  });
});
