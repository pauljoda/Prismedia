import type {
  PlaybackMode,
  PlayerQualityRung,
  QualityMode,
} from "$lib/components/video-player-types";
import type { QualityPreference } from "$lib/player/quality-preference";
import { resolvePreferredRung } from "$lib/player/quality-preference";
import { chooseInitialPlaybackMode } from "$lib/player/video-player-load";

export interface VideoPlayerSourcePolicyInput {
  src?: string;
  directSrc?: string;
  defaultPlaybackMode?: PlaybackMode;
  directAvailable: boolean;
  savedQuality: QualityPreference;
  qualityRungs: readonly PlayerQualityRung[];
}

export interface VideoPlayerSourcePolicyState {
  playbackMode: PlaybackMode;
  qualityMode: QualityMode;
  selectedRungName: string | null;
}

export function resolveInitialVideoPlayerSourcePolicy({
  src,
  directSrc,
  defaultPlaybackMode,
  directAvailable,
  savedQuality,
  qualityRungs,
}: VideoPlayerSourcePolicyInput): VideoPlayerSourcePolicyState {
  let playbackMode = chooseInitialPlaybackMode({
    src,
    directSrc,
    defaultPlaybackMode,
    directPlayable: directAvailable,
  });
  let qualityMode: QualityMode = playbackMode === "direct" ? "direct" : "auto";
  let selectedRungName: string | null = null;

  if (savedQuality === "direct" && directAvailable) {
    playbackMode = "direct";
    qualityMode = "direct";
  } else if (typeof savedQuality === "number") {
    const preferredRung = resolvePreferredRung(savedQuality, qualityRungs);
    if (preferredRung) {
      playbackMode = "hls";
      qualityMode = preferredRung;
      selectedRungName = preferredRung;
    }
  }

  return { playbackMode, qualityMode, selectedRungName };
}
