import type { AudioTrackListItemDto } from "@prismedia/contracts";
import { createOptionalContext } from "$lib/utils/context";

export type RepeatMode = "off" | "all" | "one";

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
  repeat = $state<RepeatMode>("off");
  context = $state.raw<PlaybackContext | null>(null);

  // Transport state, mirrored from the global player's <audio> element.
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
      (this.position < this.order.length - 1 || (this.repeat === "all" && this.order.length > 0)),
  );
  readonly hasPrev = $derived(this.position > 0 || (this.repeat === "all" && this.order.length > 1));

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
    this.repeat = this.repeat === "off" ? "all" : this.repeat === "all" ? "one" : "off";
  }

  /** Advances to the next track in order (wrapping when repeat-all). Returns false at the end. */
  next(): boolean {
    if (this.order.length === 0) return false;
    if (this.position < this.order.length - 1) {
      this.position++;
      return true;
    }
    if (this.repeat === "all") {
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
    if (this.repeat === "all" && this.order.length > 0) {
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
    this.playing = false;
    this.currentTime = 0;
    this.duration = 0;
    this.shuffle = false;
    this.repeat = "off";
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
