export type QualityMode = "auto" | "direct" | number | `seed:${string}`;
export type VideoPlaybackMode = "direct" | "hls";

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
}

// The player buffers deeply so a brief pause builds a large cushion that then drains while playback
// continues to fill it — the behavior users expect from streaming apps. This is safe because hls.js
// loads fragments sequentially: it stays right behind the server's single forward transcode rather
// than leaping ahead, so the server keeps serving one generation (no parallel-transcode fan-out)
// regardless of how deep the buffer grows. The byte cap, not the time length, is the practical
// limit and keeps memory bounded on high-bitrate 4K sources.
const ExtendedHlsMaxBufferLengthSeconds = 240;
const ExtendedHlsMaxMaxBufferLengthSeconds = 240;
const ExtendedHlsMaxBufferSizeBytes = 800 * 1000 * 1000;
const ExtendedHlsBackBufferLengthSeconds = 60;

// Renditions are transcoded on demand, so a fragment request can block while the server produces
// that segment — most noticeably the cold-start first segment of a 4K HDR title, which takes well
// over hls.js's 10s default time-to-first-byte. Give the transcoder generous time (and a few
// retries) so playback waits for the segment instead of aborting with a fragLoadTimeOut and stalling.
const ExtendedHlsMaxTimeToFirstByteMs = 60_000;
const ExtendedHlsMaxLoadTimeMs = 120_000;

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

function supportsAnyMime(canPlayType: CanPlayType, candidates: readonly string[]): boolean {
  return candidates.some((mime) => {
    const result = canPlayType(mime);
    return result === "probably" || result === "maybe";
  });
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

  return supportsAnyMime(canPlayType, [
    'video/mp4; codecs="hvc1"',
    'video/mp4; codecs="hev1"',
    'video/mp4; codecs="hvc1, mp4a.40.2"',
    'video/mp4; codecs="hev1, mp4a.40.2"',
  ]);
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
    fragLoadPolicy: {
      default: {
        maxTimeToFirstByteMs: ExtendedHlsMaxTimeToFirstByteMs,
        maxLoadTimeMs: ExtendedHlsMaxLoadTimeMs,
        timeoutRetry: { maxNumRetry: 2, retryDelayMs: 0, maxRetryDelayMs: 0 },
        errorRetry: { maxNumRetry: 4, retryDelayMs: 1000, maxRetryDelayMs: 8000 },
      },
    },
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
