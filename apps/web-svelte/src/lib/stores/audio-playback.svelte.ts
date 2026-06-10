import type { AudioTrackListItemDto } from "@prismedia/contracts";
import {
  MUSIC_PLAYER_MINI_SIDE,
  MUSIC_PLAYER_REPEAT_MODE,
  type MusicPlayerMiniSideCode,
  type MusicPlayerRepeatModeCode,
} from "$lib/api/generated/codes";
import { createOptionalContext } from "$lib/utils/context";

export type RepeatMode = MusicPlayerRepeatModeCode;
export type MiniPlayerSide = MusicPlayerMiniSideCode;

/** Where the queue was started from, used to label and link the now-playing artist/album. */
export interface PlaybackContext {
  albumId?: string | null;
  albumTitle?: string | null;
  artistId?: string | null;
  artistName?: string | null;
  /** Cover URL for the now-playing artwork when the track has none of its own. */
  coverUrl?: string | null;
  /** Album artwork by audio library id, used for mixed-album artist queues. */
  albumCoverUrls?: Record<string, string | null | undefined> | null;
}

/** Element-level controls the global player registers so any component can drive playback. */
export interface PlaybackController {
  toggle: () => void;
  seek: (seconds: number) => void;
  playTrack: (track: AudioTrackListItemDto) => void;
}

export interface PlayOptions {
  shuffle?: boolean;
}

function shuffleInPlace<T>(items: T[]): T[] {
  for (let i = items.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [items[i], items[j]] = [items[j], items[i]];
  }
  return items;
}

export const PRISMEDIA_AUDIO_ARTWORK_FALLBACK = "/brand/prismedia-logo.png";

export function resolveAudioArtwork(
  track: AudioTrackListItemDto | null | undefined,
  context: PlaybackContext | null | undefined,
): string {
  const albumCover = track?.libraryId ? context?.albumCoverUrls?.[track.libraryId] : null;
  return albumCover || context?.coverUrl || PRISMEDIA_AUDIO_ARTWORK_FALLBACK;
}

/**
 * Shared, app-global audio playback queue. Holds the loaded tracks (`queue`), the play `order` over
 * them (a list of indices, sequential or shuffled), and the current `position` within that order, so
 * "up next" is always knowable. Shuffle builds a persistent shuffled order and `next()` walks it —
 * it never re-randomises on advance. The store does not own the <audio> element; the global player
 * registers a {@link PlaybackController} and pushes transport state (`playing`, `currentTime`,
 * `duration`) here so other surfaces (track rows, the queue flyout) can reflect and drive playback.
 */
export class AudioPlaybackStore {
  /** The loaded source tracks. */
  queue = $state.raw<AudioTrackListItemDto[]>([]);
  /** Indices into {@link queue} defining play order (sequential or shuffled). */
  order = $state.raw<number[]>([]);
  /** Index into {@link order} of the current track, or -1 when nothing is loaded. */
  position = $state(-1);
  shuffle = $state(false);
  repeat = $state<RepeatMode>(MUSIC_PLAYER_REPEAT_MODE.off);
  context = $state.raw<PlaybackContext | null>(null);
  volume = $state(1);
  muted = $state(false);
  collapsed = $state(false);
  collapsedSide = $state<MiniPlayerSide>(MUSIC_PLAYER_MINI_SIDE.left);

  // Transport state: playIntent is the user's/restored desired state; playing is
  // only the actual state mirrored from the global player's <audio> element.
  playIntent = $state(false);
  playing = $state(false);
  currentTime = $state(0);
  duration = $state(0);

  #controller: PlaybackController | null = null;

  /** The track currently playing, or null when the queue is empty. */
  readonly currentTrack = $derived(
    this.position >= 0 && this.position < this.order.length
      ? (this.queue[this.order[this.position]!] ?? null)
      : null,
  );

  /** Tracks queued after the current one, in play order. */
  readonly upNext = $derived(
    this.order.slice(this.position + 1).map((index) => this.queue[index]!).filter(Boolean),
  );

  readonly hasNext = $derived(
    this.position >= 0 &&
      (this.position < this.order.length - 1 || (this.repeat === MUSIC_PLAYER_REPEAT_MODE.all && this.order.length > 0)),
  );
  readonly hasPrev = $derived(this.position > 0 || (this.repeat === MUSIC_PLAYER_REPEAT_MODE.all && this.order.length > 1));

  isCurrent(trackId: string): boolean {
    return this.currentTrack?.id === trackId;
  }

  /**
   * Loads a track list and starts playback. With `shuffle`, builds a shuffled order (the start track,
   * when given, stays first); otherwise plays in list order starting at the given track.
   */
  play(
    tracks: AudioTrackListItemDto[],
    startTrackId?: string | null,
    context?: PlaybackContext | null,
    options?: PlayOptions,
  ) {
    if (tracks.length === 0) return;
    this.queue = tracks;
    this.context = context ?? null;
    if (options?.shuffle !== undefined) this.shuffle = options.shuffle;
    this.playIntent = true;
    this.playing = false;

    const startIndex = startTrackId ? tracks.findIndex((track) => track.id === startTrackId) : -1;
    if (this.shuffle) {
      const indices = tracks.map((_, index) => index);
      if (startIndex >= 0) {
        const rest = shuffleInPlace(indices.filter((index) => index !== startIndex));
        this.order = [startIndex, ...rest];
      } else {
        this.order = shuffleInPlace(indices);
      }
      this.position = 0;
    } else {
      this.order = tracks.map((_, index) => index);
      this.position = startIndex >= 0 ? startIndex : 0;
    }

    const currentQueueIndex = this.order[this.position] ?? -1;
    const currentTrack = this.queue[currentQueueIndex];
    if (currentTrack) this.#controller?.playTrack(currentTrack);
  }

  /**
   * Toggles shuffle, building the list rather than randomising on advance. Turning it on keeps the
   * already-played history and current track and shuffles only the upcoming entries; turning it off
   * restores list order and keeps the current track playing.
   */
  toggleShuffle() {
    if (this.order.length === 0) {
      this.shuffle = !this.shuffle;
      return;
    }

    const currentQueueIndex = this.order[this.position]!;
    if (!this.shuffle) {
      const upcoming = shuffleInPlace(this.order.slice(this.position + 1));
      this.order = [...this.order.slice(0, this.position + 1), ...upcoming];
      this.shuffle = true;
    } else {
      this.order = this.queue.map((_, index) => index);
      this.position = this.order.indexOf(currentQueueIndex);
      this.shuffle = false;
    }
  }

  cycleRepeat() {
    this.repeat =
      this.repeat === MUSIC_PLAYER_REPEAT_MODE.off
        ? MUSIC_PLAYER_REPEAT_MODE.all
        : this.repeat === MUSIC_PLAYER_REPEAT_MODE.all
          ? MUSIC_PLAYER_REPEAT_MODE.one
          : MUSIC_PLAYER_REPEAT_MODE.off;
  }

  /** Advances to the next track in order (wrapping when repeat-all). Returns false at the end. */
  next(): boolean {
    if (this.order.length === 0) return false;
    if (this.position < this.order.length - 1) {
      this.position++;
      return true;
    }
    if (this.repeat === MUSIC_PLAYER_REPEAT_MODE.all) {
      this.position = 0;
      return true;
    }
    return false;
  }

  /** Steps back one track in order (wrapping when repeat-all). Returns false at the start. */
  prev(): boolean {
    if (this.position > 0) {
      this.position--;
      return true;
    }
    if (this.repeat === MUSIC_PLAYER_REPEAT_MODE.all && this.order.length > 0) {
      this.position = this.order.length - 1;
      return true;
    }
    return false;
  }

  /** Jumps to a specific spot in the play order (e.g. clicking an up-next item). */
  jumpTo(orderIndex: number) {
    if (orderIndex >= 0 && orderIndex < this.order.length) {
      this.position = orderIndex;
    }
  }

  /** Fully clears playback: empties the queue, resets transport, and drops shuffle/repeat. */
  clear() {
    this.queue = [];
    this.order = [];
    this.position = -1;
    this.context = null;
    this.playIntent = false;
    this.playing = false;
    this.currentTime = 0;
    this.duration = 0;
    this.shuffle = false;
    this.repeat = MUSIC_PLAYER_REPEAT_MODE.off;
    this.volume = 1;
    this.muted = false;
    this.collapsed = false;
    this.collapsedSide = MUSIC_PLAYER_MINI_SIDE.left;
  }

  /** Restores a persisted queue and player settings after page load. */
  restore(state: {
    queue: AudioTrackListItemDto[];
    order: number[];
    position: number;
    playing: boolean;
    shuffle: boolean;
    repeat: RepeatMode;
    context?: PlaybackContext | null;
    volume: number;
    muted: boolean;
    collapsed: boolean;
    collapsedSide: MiniPlayerSide;
  }) {
    const queue = state.queue;
    const order = state.order.filter((index) => index >= 0 && index < queue.length);
    this.queue = queue;
    this.order = order.length === queue.length ? order : queue.map((_, index) => index);
    this.position =
      queue.length === 0 ? -1 : Math.max(0, Math.min(state.position, Math.max(0, this.order.length - 1)));
    this.playIntent = state.playing && queue.length > 0;
    this.playing = false;
    this.shuffle = state.shuffle;
    this.repeat = state.repeat;
    this.context = state.context ?? null;
    this.currentTime = 0;
    this.duration = queue[this.order[this.position] ?? -1]?.duration ?? 0;
    this.volume = Math.max(0, Math.min(1, state.volume));
    this.muted = state.muted;
    this.collapsed = state.collapsed;
    this.collapsedSide = state.collapsedSide;
  }

  /** The global player registers element control here; returns a detach function. */
  attachController(controller: PlaybackController): () => void {
    this.#controller = controller;
    return () => {
      if (this.#controller === controller) this.#controller = null;
    };
  }

  /** Toggles play/pause on the live <audio> element via the registered controller. */
  toggle() {
    this.#controller?.toggle();
  }

  seek(seconds: number) {
    this.#controller?.seek(seconds);
  }
}

const ctx = createOptionalContext<AudioPlaybackStore | null>("AudioPlayback", null);

export function provideAudioPlayback(): AudioPlaybackStore {
  return ctx.provide(new AudioPlaybackStore())!;
}

/** Returns the playback store, or null when used outside the provider (e.g. in isolation). */
export const useAudioPlayback = ctx.use;
