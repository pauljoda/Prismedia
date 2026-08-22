import { beforeEach, describe, expect, it, vi } from "vitest";

const getMusicPlayerState = vi.hoisted(() => vi.fn());
const updateMusicPlayerProgress = vi.hoisted(() => vi.fn());
const updateMusicPlayerState = vi.hoisted(() => vi.fn());
const clearMusicPlayerState = vi.hoisted(() => vi.fn());

vi.mock("$lib/api/generated/prismedia", () => ({
  getMusicPlayerState,
  updateMusicPlayerProgress,
  updateMusicPlayerState,
  clearMusicPlayerState,
}));

vi.mock("$lib/entities/audio-track-items", () => ({
  audioPlaybackItemToTrackItem: (track: { id: string; title: string }) => ({
    id: track.id,
    title: track.title,
    duration: 120,
  }),
}));

import { fetchMusicPlayerState, saveMusicPlayerProgress, saveMusicPlayerState } from "./music-player-state";
import { MUSIC_PLAYER_MINI_SIDE, MUSIC_PLAYER_REPEAT_MODE } from "$lib/api/generated/codes";

describe("music player state API", () => {
  beforeEach(() => {
    getMusicPlayerState.mockReset();
    updateMusicPlayerProgress.mockReset().mockResolvedValue({ data: null });
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

  it("sends only current-track progress for a periodic update", async () => {
    await saveMusicPlayerProgress({
      queueTrackIds: ["track-1", "track-2"],
      order: [1, 0],
      position: 0,
      currentTime: 17,
      playing: true,
      shuffle: true,
      repeat: MUSIC_PLAYER_REPEAT_MODE.all,
      volume: 0.4,
      muted: false,
      collapsed: false,
      collapsedSide: MUSIC_PLAYER_MINI_SIDE.left,
      context: { albumTitle: "Large queue context" },
    });

    expect(updateMusicPlayerProgress).toHaveBeenCalledWith({
      currentTrackId: "track-2",
      position: 0,
      currentTime: 17,
      playing: true,
    });
    expect(updateMusicPlayerState).not.toHaveBeenCalled();
  });

  it("round-trips the logical playback owner for a mapped audio queue", async () => {
    getMusicPlayerState.mockResolvedValue({
      data: {
        tracks: [{ id: "part-2", title: "Part 2" }],
        order: [0],
        position: 0,
        currentTime: 42,
        playing: false,
        shuffle: false,
        repeat: MUSIC_PLAYER_REPEAT_MODE.off,
        volume: 0.7,
        muted: false,
        collapsed: false,
        collapsedSide: MUSIC_PLAYER_MINI_SIDE.left,
        context: {
          albumId: null,
          albumTitle: null,
          artistId: null,
          artistName: "Andy Weir",
          coverUrl: "/project-hail-mary.jpg",
          albumCoverUrls: null,
          playbackOwnerEntityId: "book-1",
          playbackOwnerTitle: "Project Hail Mary",
          playbackOwnerEntityKind: "book",
          progressMappings: [{
            itemId: "part-2",
            currentEntityId: "book-1",
            unit: "cfi",
            startIndex: 2000,
            endIndex: 4000,
            total: 10000,
            mode: "paged",
          }],
          preservesQueueOrder: true,
          supportsPlaybackRate: true,
        },
      },
    });

    const restored = await fetchMusicPlayerState();

    expect(restored.context).toMatchObject({
      playbackOwnerEntityId: "book-1",
      playbackOwnerTitle: "Project Hail Mary",
      playbackOwnerEntityKind: "book",
      progressMappings: [expect.objectContaining({ itemId: "part-2" })],
      preservesQueueOrder: true,
      supportsPlaybackRate: true,
    });

    await saveMusicPlayerState({
      queueTrackIds: ["part-2"],
      order: [0],
      position: 0,
      currentTime: 42,
      playing: false,
      shuffle: false,
      repeat: MUSIC_PLAYER_REPEAT_MODE.off,
      volume: 0.7,
      muted: false,
      collapsed: false,
      collapsedSide: MUSIC_PLAYER_MINI_SIDE.left,
      context: restored.context,
    });

    expect(updateMusicPlayerState).toHaveBeenCalledWith(expect.objectContaining({
      context: expect.objectContaining({
        playbackOwnerEntityId: "book-1",
        playbackOwnerTitle: "Project Hail Mary",
        playbackOwnerEntityKind: "book",
        progressMappings: [expect.objectContaining({ itemId: "part-2" })],
        preservesQueueOrder: true,
        supportsPlaybackRate: true,
      }),
    }));
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
