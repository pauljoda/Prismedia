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

  // Declare a range of sizes for one image so the OS picks it regardless of the slot it needs;
  // the src must be absolute (see absoluteArtworkUrl) for the artwork to render in OS controls.
  const artworkUrl = absoluteArtworkUrl(info.artwork);
  const artwork = artworkUrl
    ? [{ src: artworkUrl, sizes: "96x96 192x192 256x256 384x384 512x512" }]
    : [];
  session.metadata = new MediaMetadata({
    title: info.title,
    artist: info.artist ?? "",
    album: info.album ?? "",
    artwork,
  });
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
