import {
  PLAYBACK_MODE,
  VIDEO_PLAYBACK_METHOD,
  type PlaybackModeCode,
  type VideoPlaybackMethodCode,
} from "$lib/api/generated/codes";
import { canPlayHevcMp4 } from "$lib/player/browser-device-profile";

export type QualityMode = "auto" | "direct" | number | `seed:${string}`;
export type VideoPlaybackMode = PlaybackModeCode;

const MEDIA_LOAD_STRATEGY = {
  eager: "eager",
  onPlay: "play",
} as const;

export type MediaLoadStrategy =
  (typeof MEDIA_LOAD_STRATEGY)[keyof typeof MEDIA_LOAD_STRATEGY];

type CanPlayType = (mime: string) => CanPlayTypeResult | string;

export interface VideoLoadStateInput {
  src?: string;
  directSrc?: string;
  defaultPlaybackMode?: VideoPlaybackMode;
  directPlayable?: boolean;
  requestedMode: VideoPlaybackMode;
  prevSrcKey: string;
}

export interface VideoLoadState {
  srcKey: string;
  isNewSource: boolean;
  effectiveMode: VideoPlaybackMode;
  loadKey: string;
}

export interface AdaptiveAutoLevelSelection {
  currentLevel: -1;
  startLevel: -1;
  nextAutoLevel: -1;
}

export interface HlsLoadPolicy {
  default: {
    maxTimeToFirstByteMs: number;
    maxLoadTimeMs: number;
    timeoutRetry: { maxNumRetry: number; retryDelayMs: number; maxRetryDelayMs: number };
    errorRetry: { maxNumRetry: number; retryDelayMs: number; maxRetryDelayMs: number };
  };
}

export interface AdaptiveHlsBufferConfig {
  backBufferLength: number;
  capLevelToPlayerSize: boolean;
  frontBufferFlushThreshold: number;
  maxBufferLength: number;
  maxMaxBufferLength: number;
  maxBufferSize: number;
  startLevel: number;
  startPosition: number;
  fragLoadPolicy: HlsLoadPolicy;
  manifestLoadPolicy: HlsLoadPolicy;
  playlistLoadPolicy: HlsLoadPolicy;
}

// The player keeps a modest forward buffer rather than racing far ahead. A deep buffer made the
// player request segments well ahead of the server's linear transcode frontier, which forced the
// reuse window (HlsAssetService.ActiveGenerationReuseWindowSegments) to stay correspondingly wide —
// and a wide reuse window is exactly what makes a forward seek/resume slow, because the player
// attaches to the running job and waits while it grinds to the target instead of restarting there.
// Keeping the buffer near the reuse window lets that window be tight, so a forward seek beyond it
// cold-starts a fresh transcode at the seek point (fast). These two values are coupled and must move
// together. The byte cap still bounds memory on high-bitrate 4K sources.
const ExtendedHlsMaxBufferLengthSeconds = 30;
const ExtendedHlsMaxMaxBufferLengthSeconds = 30;
const ExtendedHlsMaxBufferSizeBytes = 800 * 1000 * 1000;
const ExtendedHlsBackBufferLengthSeconds = 60;

// Renditions are transcoded on demand, so a fragment request can block while the server produces
// that segment — most noticeably the cold-start first segment of a 4K HDR title, which takes well
// over hls.js's 10s default time-to-first-byte. Give the transcoder generous time (and a few
// retries) so playback waits for the segment instead of aborting with a fragLoadTimeOut and stalling.
const ExtendedHlsMaxTimeToFirstByteMs = 60_000;
const ExtendedHlsMaxLoadTimeMs = 120_000;

// The same generosity must cover the MANIFEST and PLAYLIST loads, not just fragments. The remux source
// is a bare media playlist (no master), and on a cold first play the server briefly waits for ffmpeg's
// first event playlist while the full VOD playlist is built off-thread. hls.js's default 20s manifest /
// playlist cap (10s time-to-first-byte) would abort that with "a network timeout occurred while loading
// manifest". Reusing the fragment timeouts here keeps the cold open from failing.
const extendedHlsLoadPolicy = (): HlsLoadPolicy => ({
  default: {
    maxTimeToFirstByteMs: ExtendedHlsMaxTimeToFirstByteMs,
    maxLoadTimeMs: ExtendedHlsMaxLoadTimeMs,
    timeoutRetry: { maxNumRetry: 2, retryDelayMs: 0, maxRetryDelayMs: 0 },
    errorRetry: { maxNumRetry: 4, retryDelayMs: 1000, maxRetryDelayMs: 8000 },
  },
});

export interface AdaptiveSeekPlanInput {
  streamMode: VideoPlaybackMode;
  target: number;
  seekableEnd: number | null;
  hasManagedHls: boolean;
}

export interface AdaptiveSeekPlan {
  currentTime: number;
  deferredSeekTarget: number | null;
  hlsStartLoadAt: number | null;
}

export interface PlaybackErrorFallbackInput {
  effectiveMode: VideoPlaybackMode;
  hlsSrc?: string;
  directSrc?: string;
  directPlayable?: boolean;
  directFailed?: boolean;
}

export function requestedModeFromQualityMode(
  qualityMode: QualityMode,
): VideoPlaybackMode {
  return qualityMode === "direct" ? "direct" : "hls";
}

export function chooseInitialPlaybackMode({
  src,
  directSrc,
  defaultPlaybackMode,
  directPlayable = true,
}: {
  src?: string;
  directSrc?: string;
  defaultPlaybackMode?: VideoPlaybackMode;
  directPlayable?: boolean;
}): VideoPlaybackMode {
  if (defaultPlaybackMode === "hls" && src) {
    return "hls";
  }

  if (directSrc && directPlayable) {
    return "direct";
  }

  return "hls";
}

function normalizeCodec(codec: string | null | undefined): string {
  return codec?.trim().toLowerCase() ?? "";
}

export function isHevcCodec(codec: string | null | undefined): boolean {
  const normalized = normalizeCodec(codec);
  return normalized === "hevc" || normalized === "h265" || normalized === "h.265";
}

export function canUseDirectPlayback({
  directSrc,
  codec,
  canPlayType,
}: {
  directSrc?: string;
  codec?: string | null;
  canPlayType?: CanPlayType;
}): boolean {
  if (!directSrc) return false;
  if (!isHevcCodec(codec)) return true;
  if (!canPlayType) return false;

  return canPlayHevcMp4(canPlayType);
}

/** Resolves the delivery method from the source the player actually selected, not only negotiation. */
export function resolveEffectivePlaybackMethod({
  effectiveMode,
  negotiatedMethod,
  forcedTranscode,
}: {
  effectiveMode: VideoPlaybackMode;
  negotiatedMethod: VideoPlaybackMethodCode;
  forcedTranscode: boolean;
}): VideoPlaybackMethodCode {
  if (forcedTranscode) return VIDEO_PLAYBACK_METHOD.transcode;
  if (effectiveMode === PLAYBACK_MODE.direct) return VIDEO_PLAYBACK_METHOD.direct;
  if (negotiatedMethod === VIDEO_PLAYBACK_METHOD.remux) return VIDEO_PLAYBACK_METHOD.remux;
  return VIDEO_PLAYBACK_METHOD.transcode;
}

/**
 * Allows the browser to pre-load only metadata for direct files while keeping adaptive sources
 * dormant until playback is requested. Eagerly attaching an HLS source could start an on-demand
 * remux or transcode for a video the viewer never plays.
 */
export function resolveMediaLoadStrategy({
  effectiveMode,
  hasPlayIntent,
}: {
  effectiveMode: VideoPlaybackMode;
  hasPlayIntent: boolean;
}): MediaLoadStrategy {
  return effectiveMode === PLAYBACK_MODE.direct || hasPlayIntent
    ? MEDIA_LOAD_STRATEGY.eager
    : MEDIA_LOAD_STRATEGY.onPlay;
}

/** Stops the browser's current media request before its owning player is removed. */
export function abortVideoElementLoad(video: HTMLVideoElement | null): void {
  if (!video) return;
  video.pause();
  video.removeAttribute("src");
  // Resetting the element after clearing src terminates any outstanding metadata range request.
  video.load();
}

export function computeVideoLoadState({
  src,
  directSrc,
  defaultPlaybackMode,
  directPlayable = true,
  requestedMode,
  prevSrcKey,
}: VideoLoadStateInput): VideoLoadState {
  const srcKey = `${src ?? ""}|${directSrc ?? ""}`;
  const isNewSource = srcKey !== prevSrcKey;
  const initialMode = chooseInitialPlaybackMode({
    src,
    directSrc,
    defaultPlaybackMode,
    directPlayable,
  });
  const effectiveMode = isNewSource ? initialMode : requestedMode;

  return {
    srcKey,
    isNewSource,
    effectiveMode,
    loadKey: `${srcKey}|${effectiveMode}`,
  };
}

export function adaptiveAutoLevelSelection(): AdaptiveAutoLevelSelection {
  return {
    currentLevel: -1,
    startLevel: -1,
    nextAutoLevel: -1,
  };
}

export function adaptiveHlsBufferConfig(): AdaptiveHlsBufferConfig {
  return {
    backBufferLength: ExtendedHlsBackBufferLengthSeconds,
    capLevelToPlayerSize: false,
    frontBufferFlushThreshold: Infinity,
    maxBufferLength: ExtendedHlsMaxBufferLengthSeconds,
    maxMaxBufferLength: ExtendedHlsMaxMaxBufferLengthSeconds,
    maxBufferSize: ExtendedHlsMaxBufferSizeBytes,
    startLevel: -1,
    startPosition: 0,
    fragLoadPolicy: extendedHlsLoadPolicy(),
    manifestLoadPolicy: extendedHlsLoadPolicy(),
    playlistLoadPolicy: extendedHlsLoadPolicy(),
  };
}

export function fallbackPlaybackModeForError({
  effectiveMode,
  hlsSrc,
  directSrc,
  directPlayable = true,
  directFailed = false,
}: PlaybackErrorFallbackInput): VideoPlaybackMode | null {
  if (effectiveMode === "direct" && hlsSrc) {
    return "hls";
  }

  if (effectiveMode === "hls" && directSrc && directPlayable && !directFailed) {
    return "direct";
  }

  return null;
}

export function hlsStatusUrlForSrc(src: string): string | null {
  if (!/\/video-stream\/[^/]+\/hls2\/master\.m3u8(?:\?.*)?$/.test(src)) {
    return null;
  }

  const statusUrl = src.replace(/\/master\.m3u8(\?.*)?$/, "/status$1");
  return statusUrl === src ? null : statusUrl;
}

export function normalizeInitialPlaybackTime(time: number, duration: number): number {
  if (!Number.isFinite(time)) return 0;
  const safeTime = Math.max(0, time);
  return duration > 0 ? Math.min(safeTime, duration) : safeTime;
}

export function adaptiveSeekPlan({
  streamMode,
  target,
  seekableEnd,
  hasManagedHls,
}: AdaptiveSeekPlanInput): AdaptiveSeekPlan {
  if (streamMode === "hls" && hasManagedHls) {
    return {
      currentTime: target,
      deferredSeekTarget: null,
      hlsStartLoadAt: target,
    };
  }

  if (
    streamMode === "hls" &&
    seekableEnd !== null &&
    Number.isFinite(seekableEnd) &&
    target > seekableEnd + 0.5
  ) {
    return {
      currentTime: Math.max(0, seekableEnd - 0.5),
      deferredSeekTarget: target,
      hlsStartLoadAt: null,
    };
  }

  return {
    currentTime: target,
    deferredSeekTarget: null,
    hlsStartLoadAt: null,
  };
}
