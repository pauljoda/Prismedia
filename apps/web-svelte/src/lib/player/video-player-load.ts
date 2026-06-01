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

export interface AdaptiveHlsBufferConfig {
  backBufferLength: number;
  capLevelToPlayerSize: boolean;
  frontBufferFlushThreshold: number;
  maxBufferLength: number;
  maxMaxBufferLength: number;
  maxBufferSize: number;
  startLevel: number;
  startPosition: number;
}

// Look-ahead is intentionally bounded. Renditions are transcoded on demand by a single forward
// ffmpeg per stream, so a client that buffers far ahead forces the server to spawn extra parallel
// transcodes for the not-yet-reached segments; on hardware encoders (e.g. VideoToolbox) those
// sessions contend for one encoder and every one drops below real time, which stalls playback.
// Keeping the forward buffer well under the server's generation reuse window (72s) means the
// client only ever requests segments the single running transcode already has or is about to
// produce, so playback stays ahead without fanning out the transcoder.
const ExtendedHlsMaxBufferLengthSeconds = 30;
const ExtendedHlsMaxMaxBufferLengthSeconds = 48;
const ExtendedHlsMaxBufferSizeBytes = 300 * 1000 * 1000;

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
    backBufferLength: Infinity,
    capLevelToPlayerSize: false,
    frontBufferFlushThreshold: Infinity,
    maxBufferLength: ExtendedHlsMaxBufferLengthSeconds,
    maxMaxBufferLength: ExtendedHlsMaxMaxBufferLengthSeconds,
    maxBufferSize: ExtendedHlsMaxBufferSizeBytes,
    startLevel: -1,
    startPosition: 0,
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
