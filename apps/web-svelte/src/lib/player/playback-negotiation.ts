import { createVideoPlaybackPlan } from "$lib/api/playback";
import { extractVideoPlayerProps } from "$lib/entities/video-capabilities";
import { getBrowserDeviceProfile } from "$lib/player/browser-device-profile";
import type { EntityCapability, VideoPlaybackPlanResponse } from "$lib/api/generated/model";

/**
 * Negotiates a playback source with Prismedia's native playback planner.
 *
 * Shared by the video and movie detail routes so the device-profile advertisement and the
 * force-transcode recovery path stay identical across both. Returns null on any failure so
 * callers can fall back to their existing source props without throwing.
 *
 * @param videoId The media entity id.
 * @param opts.sessionId Existing play session to continue, if any.
 * @param opts.audioStreamIndex Preferred audio stream index.
 * @param opts.forceTranscode When true, disables direct play and stream-copy so the server must
 *   return a re-encoded H.264 stream the browser can always decode — the player's last-resort
 *   recovery after a fatal decode error on an optimistic remux/direct source.
 */
export async function loadPlaybackPlan(
  videoId: string,
  opts: {
    sessionId?: string | null;
    audioStreamIndex?: number | null;
    forceTranscode?: boolean;
  } = {},
): Promise<VideoPlaybackPlanResponse | null> {
  try {
    return await createVideoPlaybackPlan(videoId, {
      enableDirectPlay: !opts.forceTranscode,
      enableDirectStream: !opts.forceTranscode,
      enableTranscoding: true,
      sessionId: opts.sessionId ?? undefined,
      audioStreamIndex: opts.audioStreamIndex ?? undefined,
      // Tell the server what this browser can actually decode so it only transcodes when it must.
      profile: getBrowserDeviceProfile(),
    });
  } catch {
    return null;
  }
}

/**
 * Player `onForceTranscode` callback: after a fatal decode error, re-negotiate a
 * guaranteed-playable transcode and return its URL. The player swaps to it in place (preserving
 * position) via its own forced-transcode source, so this intentionally does NOT mutate the page's
 * `playbackPlan` — that would recompute the `src` prop and race the in-place swap. Same
 * source entity, same session: progress reporting is unaffected.
 *
 * @returns The transcode playlist URL, or null if no compatible stream is available.
 */
export async function negotiateForceTranscodeSrc(
  video: { id: string; capabilities: EntityCapability[] },
  audioStreamIndex: number | null,
  sessionId: string | null | undefined,
): Promise<string | null> {
  const plan = await loadPlaybackPlan(video.id, {
    sessionId,
    audioStreamIndex,
    forceTranscode: true,
  });
  if (!plan) return null;
  const props = extractVideoPlayerProps(video.id, video.capabilities, plan, audioStreamIndex);
  return props.src || null;
}
