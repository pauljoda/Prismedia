/**
 * Thin wrapper around the browser {@link https://developer.mozilla.org/docs/Web/API/Media_Session_API Media Session API}.
 *
 * Lets Prismedia tell the operating system what is playing (title, artist, album, artwork) and which
 * transport controls are available, so the OS media keys, lock screen, notification shade, and
 * Bluetooth controls show the real now-playing media instead of a generic browser tab title. Every
 * function is a no-op when the API is unavailable (server-side rendering or unsupported browsers).
 */

/** Identifying metadata for the media currently playing. */
export interface MediaSessionTrackInfo {
  /** Title of the track, video, or episode. */
  title: string;
  /** Performing artist, show, or studio. Optional. */
  artist?: string | null;
  /** Album or collection the media belongs to. Optional. */
  album?: string | null;
  /** Absolute or root-relative URL of cover art / thumbnail. Optional. */
  artwork?: string | null;
}

/** Transport actions Prismedia can respond to from the OS media controls. */
export interface MediaSessionHandlers {
  play?: () => void;
  pause?: () => void;
  /** Skip to the previous track. Omit (or null) when there is none. */
  previoustrack?: (() => void) | null;
  /** Skip to the next track. Omit (or null) when there is none. */
  nexttrack?: (() => void) | null;
  /** Seek to an absolute position in seconds. */
  seekto?: (time: number) => void;
  /** Seek backward by the given number of seconds (default 10). */
  seekbackward?: (offset: number) => void;
  /** Seek forward by the given number of seconds (default 10). */
  seekforward?: (offset: number) => void;
  /** Stop playback and dismiss the session. */
  stop?: () => void;
}

/** All transport actions this module knows how to register, used to clear handlers on teardown. */
const ALL_ACTIONS: MediaSessionAction[] = [
  "play",
  "pause",
  "previoustrack",
  "nexttrack",
  "seekto",
  "seekbackward",
  "seekforward",
  "stop",
];

/** Returns the platform {@link MediaSession}, or null when the API is unavailable. */
function mediaSession(): MediaSession | null {
  if (typeof navigator === "undefined") return null;
  return navigator.mediaSession ?? null;
}

/**
 * Resolves an artwork URL to an absolute URL. The OS Now-Playing handler fetches artwork outside
 * the page context and does not reliably resolve root-relative paths (notably on WebKit), so cover
 * art served from a path like `/assets/...` must be made absolute or it silently fails to appear.
 */
export function absoluteArtworkUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  if (/^https?:\/\//i.test(url) || url.startsWith("data:") || url.startsWith("blob:")) return url;
  if (typeof window === "undefined") return url;
  try {
    return new URL(url, window.location.origin).href;
  } catch {
    return url;
  }
}

/** Best-effort MIME type from a URL's file extension; undefined when unknown. */
function guessImageType(url: string): string | undefined {
  const path = url.split("?")[0].toLowerCase();
  if (path.endsWith(".jpg") || path.endsWith(".jpeg")) return "image/jpeg";
  if (path.endsWith(".png")) return "image/png";
  if (path.endsWith(".webp")) return "image/webp";
  if (path.endsWith(".avif")) return "image/avif";
  if (path.endsWith(".gif")) return "image/gif";
  return undefined;
}

/** Standard artwork slot sizes the OS may request; declaring several improves WebKit/iOS pickup. */
const ARTWORK_SIZES = ["96x96", "128x128", "192x192", "256x256", "384x384", "512x512"];

/**
 * Builds a Media Session artwork list from a single cover/poster URL. The URL is made absolute (the
 * OS handler fetches it outside the page) and emitted as one well-formed entry per standard size,
 * all pointing at the same image. Multiple single-size entries (rather than one entry with a
 * space-separated `sizes` string) is the broadly compatible shape: WebKit throws from the
 * MediaMetadata constructor on the multi-token form, but accepts these, and iOS only renders
 * artwork when a size it wants is offered. Returns an empty list when no URL is available.
 */
export function buildMediaArtwork(url: string | null | undefined): MediaImage[] {
  const absolute = absoluteArtworkUrl(url);
  if (!absolute) return [];
  const type = guessImageType(absolute);
  return ARTWORK_SIZES.map((sizes) =>
    type ? { src: absolute, sizes, type } : { src: absolute, sizes },
  );
}

/** Safely registers (or clears, when fn is null) one action handler, ignoring unsupported actions. */
function setHandler(
  session: MediaSession,
  action: MediaSessionAction,
  fn: MediaSessionActionHandler | null,
): void {
  try {
    session.setActionHandler(action, fn);
  } catch {
    // Some browsers throw on actions they do not support; treat that as "not available".
  }
}

/**
 * Publishes now-playing metadata to the OS. Pass null to clear it.
 *
 * @param info Title, artist, album, and artwork of the current media, or null to clear.
 */
export function setMediaSessionMetadata(info: MediaSessionTrackInfo | null): void {
  const session = mediaSession();
  if (!session || typeof MediaMetadata === "undefined") return;

  if (!info) {
    session.metadata = null;
    return;
  }

  const artwork = buildMediaArtwork(info.artwork);
  const build = (withArtwork: boolean) =>
    new MediaMetadata({
      title: info.title,
      artist: info.artist ?? "",
      album: info.album ?? "",
      artwork: withArtwork ? artwork : [],
    });

  // Some engines (notably WebKit) can reject an artwork entry and throw from the MediaMetadata
  // constructor; fall back to metadata without artwork so the title and artist still appear.
  try {
    session.metadata = build(artwork.length > 0);
  } catch {
    try {
      session.metadata = build(false);
    } catch {
      // Give up rather than letting a metadata failure break playback.
    }
  }
}

/**
 * Registers transport-control handlers and returns a cleanup function that clears all of them.
 *
 * @param handlers The actions to respond to. Actions whose handler is omitted or null are cleared.
 * @returns A function that removes every handler this call registered.
 */
export function setMediaSessionHandlers(handlers: MediaSessionHandlers): () => void {
  const session = mediaSession();
  if (!session) return () => {};

  setHandler(session, "play", handlers.play ?? null);
  setHandler(session, "pause", handlers.pause ?? null);
  setHandler(session, "previoustrack", handlers.previoustrack ?? null);
  setHandler(session, "nexttrack", handlers.nexttrack ?? null);
  setHandler(session, "stop", handlers.stop ?? null);

  const { seekto, seekbackward, seekforward } = handlers;
  setHandler(session, "seekto", seekto ? (details) => {
    if (typeof details.seekTime === "number") seekto(details.seekTime);
  } : null);
  setHandler(session, "seekbackward", seekbackward
    ? (details) => seekbackward(details.seekOffset ?? 10)
    : null);
  setHandler(session, "seekforward", seekforward
    ? (details) => seekforward(details.seekOffset ?? 10)
    : null);

  return () => {
    const active = mediaSession();
    if (!active) return;
    for (const action of ALL_ACTIONS) setHandler(active, action, null);
  };
}

/** Reflects whether playback is active so the OS shows the correct play/pause state. */
export function setMediaSessionPlaybackState(state: MediaSessionPlaybackState): void {
  const session = mediaSession();
  if (!session) return;
  session.playbackState = state;
}

/**
 * Reports the current playback position so the OS can render an accurate scrubber.
 *
 * Invalid durations (zero, infinite, or NaN — common while a track is still loading) are ignored
 * rather than throwing.
 */
export function setMediaSessionPosition(
  duration: number,
  position: number,
  playbackRate = 1,
): void {
  const session = mediaSession();
  if (!session || typeof session.setPositionState !== "function") return;
  if (!Number.isFinite(duration) || duration <= 0) return;

  try {
    session.setPositionState({
      duration,
      position: Math.max(0, Math.min(position, duration)),
      playbackRate: playbackRate > 0 ? playbackRate : 1,
    });
  } catch {
    // Ignore browsers that reject the position state (e.g. mismatched duration mid-seek).
  }
}
