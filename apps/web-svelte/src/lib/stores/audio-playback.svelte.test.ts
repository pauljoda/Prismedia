import { describe, expect, it } from "vitest";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import { MUSIC_PLAYER_MINI_SIDE, MUSIC_PLAYER_REPEAT_MODE } from "$lib/api/generated/codes";
import {
  AudioPlaybackStore,
  PRISMEDIA_AUDIO_ARTWORK_FALLBACK,
  resolveAudioArtist,
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

/** One 600-second file with chapters at 0, 200 and 450 seconds (declared ends leave small gaps). */
function chapteredFile(id: string): AudioTrackListItemDto {
  return {
    id,
    title: `${id} file`,
    duration: 600,
    embeddedArtist: null,
    embeddedAlbum: null,
    waveformPath: null,
    chapters: [
      { markerId: `${id}-c3`, title: "Three", startSeconds: 450, endSeconds: null },
      { markerId: `${id}-c1`, title: "One", startSeconds: 2, endSeconds: 195 },
      { markerId: `${id}-c2`, title: "Two", startSeconds: 200, endSeconds: 440 },
    ],
  } as unknown as AudioTrackListItemDto;
}

const audiobook = { playbackOwnerEntityId: "book-1", preservesQueueOrder: true, supportsPlaybackRate: true };

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

  it("prefers a track artist over the album artist without linking to the wrong artist entity", () => {
    const [track] = tracks(1);
    track.embeddedArtist = "Taylor Swift";

    expect(resolveAudioArtist(track, {
      artistId: "album-artist-id",
      artistName: "Randy Newman",
    })).toEqual({ name: "Taylor Swift", artistId: null });
  });

  it("uses the album artist as fallback and keeps its entity link", () => {
    const [track] = tracks(1);

    expect(resolveAudioArtist(track, {
      artistId: "album-artist-id",
      artistName: "Randy Newman",
    })).toEqual({ name: "Randy Newman", artistId: "album-artist-id" });
  });

  it("restores persisted play intent without reporting active playback before the audio element starts", () => {
    const store = new AudioPlaybackStore();
    store.restore({
      queue: tracks(3),
      order: [2, 0, 1],
      position: 1,
      currentTime: 27,
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
    expect(store.currentTime).toBe(27);
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

  it("restores capability-locked queues in source order while keeping the current item", () => {
    const store = new AudioPlaybackStore();
    store.restore({
      queue: tracks(3),
      order: [2, 0, 1],
      position: 0,
      currentTime: 27,
      playing: true,
      shuffle: true,
      repeat: MUSIC_PLAYER_REPEAT_MODE.off,
      context: {
        playbackOwnerEntityId: "book-1",
        playbackOwnerTitle: "Book",
        playbackOwnerEntityKind: "book",
        preservesQueueOrder: true,
        supportsPlaybackRate: true,
      },
      volume: 1,
      muted: false,
      collapsed: false,
      collapsedSide: MUSIC_PLAYER_MINI_SIDE.left,
    });

    expect(ids(store)).toEqual(["t1", "t2", "t3"]);
    expect(store.currentTrack?.id).toBe("t3");
    expect(store.position).toBe(2);
    expect(store.shuffle).toBe(false);

    store.toggleShuffle();
    expect(store.shuffle).toBe(false);
  });

  it("locks a newly started queue when its playback capability preserves source order", () => {
    const store = new AudioPlaybackStore();
    store.shuffle = true;

    store.play(tracks(3), "t2", { preservesQueueOrder: true }, { shuffle: true });

    expect(ids(store)).toEqual(["t1", "t2", "t3"]);
    expect(store.currentTrack?.id).toBe("t2");
    expect(store.shuffle).toBe(false);
  });

  it("records play intent immediately when starting a queue", () => {
    const store = new AudioPlaybackStore();

    store.play(tracks(2), "t1");

    expect(store.playIntent).toBe(true);
    expect(store.playing).toBe(false);
  });

  it("starts a selected concrete part at a supplied local resume offset", () => {
    const store = new AudioPlaybackStore();

    store.play(tracks(3), "t2", null, { startSeconds: 37 });

    expect(store.currentTrack?.id).toBe("t2");
    expect(store.currentTime).toBe(37);
  });

  it("presents an ordered queue's embedded chapters as contiguous entries of their file", () => {
    const store = new AudioPlaybackStore();
    store.play([chapteredFile("a"), chapteredFile("b")], "a", audiobook, { startSeconds: 197 });

    // The gap after a declared end belongs to the chapter before it; the first chapter starts the file.
    expect(store.currentChapter?.title).toBe("One");
    expect(store.entryStart).toBe(0);
    expect(store.entryDuration).toBe(200);
    expect(store.entryTime).toBe(197);
    expect(store.nextChapter?.title).toBe("Two");
    expect(store.previousChapter).toBeNull();

    store.currentTime = 500;
    expect(store.entryTitle).toBe("Three");
    expect(store.entryTime).toBe(50);
    expect(store.entryDuration).toBe(150);
    expect(store.nextChapter).toBeNull();
    expect(store.hasNext).toBe(true);
    expect(store.upNextEntries.map((entry) => `${entry.track.id}:${entry.chapter?.title}`))
      .toEqual(["b:One", "b:Two", "b:Three"]);

    store.currentTime = 250;
    expect(store.upNextEntries.map((entry) => `${entry.track.id}:${entry.chapter?.title}`))
      .toEqual(["a:Three", "b:One", "b:Two", "b:Three"]);
    expect(store.steppingBackStart(store.queue[1])).toBe(450);
  });

  it("decides where Previous and repeat-one land inside a chaptered file", () => {
    const store = new AudioPlaybackStore();
    store.play([chapteredFile("a"), chapteredFile("b")], "b", audiobook, { startSeconds: 260 });

    // Past the restart threshold the chapter restarts; inside it Previous steps back a chapter.
    expect(store.previousChapterStart(260)).toBe(200);
    expect(store.previousChapterStart(202)).toBe(0);
    // From the first chapter, Previous leaves the file.
    expect(store.previousChapterStart(1)).toBeNull();

    expect(store.repeatedChapterStart(450)).toBeNull();
    store.repeat = MUSIC_PLAYER_REPEAT_MODE.one;
    expect(store.repeatedChapterStart(449)).toBeNull();
    expect(store.repeatedChapterStart(450)).toBe(200);
  });

  it("keeps a single-chapter file and every music queue playing whole", () => {
    const store = new AudioPlaybackStore();
    store.play([chapteredFile("a")], "a", null, { startSeconds: 300 });

    expect(store.currentChapter).toBeNull();
    expect(store.entryTitle).toBe("a file");
    expect(store.entryDuration).toBe(600);
    expect(store.upNextEntries).toEqual([]);

    const single = { ...chapteredFile("s"), chapters: [chapteredFile("s").chapters![0]!] };
    store.play([single], "s", audiobook);
    expect(store.currentChapter).toBeNull();
    expect(store.steppingBackStart(single)).toBe(0);
  });

  it("clears the queue without resetting browser audio output preferences", () => {
    const store = new AudioPlaybackStore();
    store.volume = 0.3;
    store.muted = true;
    store.collapsedSide = MUSIC_PLAYER_MINI_SIDE.right;
    store.play(tracks(2), "t1");

    store.clear();

    expect(store.queue).toEqual([]);
    expect(store.volume).toBe(0.3);
    expect(store.muted).toBe(true);
    expect(store.collapsedSide).toBe(MUSIC_PLAYER_MINI_SIDE.right);
  });
});
