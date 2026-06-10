import { describe, expect, it } from "vitest";
import type { AudioTrackListItemDto } from "@prismedia/contracts";
import { MUSIC_PLAYER_MINI_SIDE, MUSIC_PLAYER_REPEAT_MODE } from "$lib/api/generated/codes";
import {
  AudioPlaybackStore,
  PRISMEDIA_AUDIO_ARTWORK_FALLBACK,
  resolveAudioArtwork,
} from "./audio-playback.svelte";

function tracks(count: number): AudioTrackListItemDto[] {
  return Array.from({ length: count }, (_, i) => ({
    id: `t${i + 1}`,
    title: `Track ${i + 1}`,
    duration: 100,
    embeddedArtist: null,
    embeddedAlbum: null,
    waveformPath: null,
  }) as unknown as AudioTrackListItemDto);
}

const ids = (store: AudioPlaybackStore) => store.order.map((i) => store.queue[i]!.id);
const upNextIds = (store: AudioPlaybackStore) => store.upNext.map((t) => t.id);

describe("AudioPlaybackStore", () => {
  it("plays in list order starting at the chosen track", () => {
    const store = new AudioPlaybackStore();
    store.play(tracks(4), "t3");
    expect(store.currentTrack?.id).toBe("t3");
    expect(ids(store)).toEqual(["t1", "t2", "t3", "t4"]);
    expect(upNextIds(store)).toEqual(["t4"]);
  });

  it("passes a newly-started current track to the mounted controller", () => {
    const store = new AudioPlaybackStore();
    const played: string[] = [];
    store.attachController({
      toggle: () => {},
      seek: () => {},
      playTrack: (track) => played.push(track.id),
    });

    store.play(tracks(4), "t3");

    expect(played).toEqual(["t3"]);
  });

  it("next and prev walk the order; repeat-off stops at the ends", () => {
    const store = new AudioPlaybackStore();
    store.play(tracks(3), "t1");
    expect(store.next()).toBe(true);
    expect(store.currentTrack?.id).toBe("t2");
    expect(store.next()).toBe(true);
    expect(store.currentTrack?.id).toBe("t3");
    expect(store.next()).toBe(false); // at end, repeat off
    expect(store.currentTrack?.id).toBe("t3");
    expect(store.prev()).toBe(true);
    expect(store.currentTrack?.id).toBe("t2");
  });

  it("repeat-all wraps at both ends", () => {
    const store = new AudioPlaybackStore();
    store.play(tracks(3), "t3");
    store.repeat = MUSIC_PLAYER_REPEAT_MODE.all;
    expect(store.next()).toBe(true);
    expect(store.currentTrack?.id).toBe("t1");
    expect(store.prev()).toBe(true);
    expect(store.currentTrack?.id).toBe("t3");
  });

  it("starting with shuffle keeps the chosen track first, then a permutation of the rest", () => {
    const store = new AudioPlaybackStore();
    store.play(tracks(6), "t2", null, { shuffle: true });
    expect(store.currentTrack?.id).toBe("t2");
    expect(ids(store)[0]).toBe("t2");
    expect([...ids(store)].sort()).toEqual(["t1", "t2", "t3", "t4", "t5", "t6"]);
  });

  it("toggling shuffle on keeps history and current, shuffling only the upcoming entries", () => {
    const store = new AudioPlaybackStore();
    store.play(tracks(6), "t1");
    store.next(); // now on t2 (history: t1, t2)
    const before = ids(store);
    store.toggleShuffle();
    expect(store.shuffle).toBe(true);
    // History + current unchanged.
    expect(ids(store).slice(0, 2)).toEqual(before.slice(0, 2));
    expect(store.currentTrack?.id).toBe("t2");
    // Upcoming is a permutation of the same remaining tracks.
    expect([...upNextIds(store)].sort()).toEqual(["t3", "t4", "t5", "t6"]);
  });

  it("toggling shuffle off restores list order and keeps the current track", () => {
    const store = new AudioPlaybackStore();
    store.play(tracks(5), "t1", null, { shuffle: true });
    store.next();
    const current = store.currentTrack?.id;
    store.toggleShuffle();
    expect(store.shuffle).toBe(false);
    expect(ids(store)).toEqual(["t1", "t2", "t3", "t4", "t5"]);
    expect(store.currentTrack?.id).toBe(current);
  });

  it("jumpTo moves to a specific order position", () => {
    const store = new AudioPlaybackStore();
    store.play(tracks(4), "t1");
    store.jumpTo(2);
    expect(store.currentTrack?.id).toBe("t3");
    store.jumpTo(99); // out of range — ignored
    expect(store.currentTrack?.id).toBe("t3");
  });

  it("resolves current-track album artwork before artist artwork", () => {
    const [track] = tracks(1);
    track.libraryId = "album-1";

    expect(resolveAudioArtwork(track, {
      coverUrl: "/artist.jpg",
      albumCoverUrls: { "album-1": "/album.jpg" },
    })).toBe("/album.jpg");
  });

  it("falls back from missing album artwork to artist artwork and then Prismedia logo", () => {
    const [track] = tracks(1);
    track.libraryId = "album-1";

    expect(resolveAudioArtwork(track, {
      coverUrl: "/artist.jpg",
      albumCoverUrls: { "album-1": null },
    })).toBe("/artist.jpg");
    expect(resolveAudioArtwork(track, null)).toBe(PRISMEDIA_AUDIO_ARTWORK_FALLBACK);
  });

  it("restores persisted play intent without reporting active playback before the audio element starts", () => {
    const store = new AudioPlaybackStore();
    store.restore({
      queue: tracks(3),
      order: [2, 0, 1],
      position: 1,
      playing: true,
      shuffle: true,
      repeat: MUSIC_PLAYER_REPEAT_MODE.one,
      context: { albumTitle: "Saved album" },
      volume: 0.42,
      muted: true,
      collapsed: true,
      collapsedSide: MUSIC_PLAYER_MINI_SIDE.right,
    });

    expect(ids(store)).toEqual(["t3", "t1", "t2"]);
    expect(store.currentTrack?.id).toBe("t1");
    expect(store.playIntent).toBe(true);
    expect(store.playing).toBe(false);
    expect(store.shuffle).toBe(true);
    expect(store.repeat).toBe(MUSIC_PLAYER_REPEAT_MODE.one);
    expect(store.context?.albumTitle).toBe("Saved album");
    expect(store.volume).toBe(0.42);
    expect(store.muted).toBe(true);
    expect(store.collapsed).toBe(true);
    expect(store.collapsedSide).toBe(MUSIC_PLAYER_MINI_SIDE.right);
  });

  it("records play intent immediately when starting a queue", () => {
    const store = new AudioPlaybackStore();

    store.play(tracks(2), "t1");

    expect(store.playIntent).toBe(true);
    expect(store.playing).toBe(false);
  });
});
