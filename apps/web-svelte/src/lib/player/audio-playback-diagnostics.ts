import {
  AUDIO_PLAYBACK_DIAGNOSTIC_EVENT,
  AUDIO_PLAYBACK_PAUSE_SOURCE,
  type AudioPlaybackDiagnosticEventCode,
  type AudioPlaybackPauseSourceCode,
} from "$lib/api/generated/codes";
import type { AudioPlaybackDiagnosticRequest } from "$lib/api/generated/model";

export interface AudioPlaybackDiagnosticSnapshot {
  trackId: string;
  positionSeconds: number;
  durationSeconds: number | null;
  bufferedAheadSeconds: number;
  readyState: number;
  networkState: number;
  paused: boolean;
  ended: boolean;
  playIntent: boolean;
  documentVisible: boolean;
  documentHasFocus: boolean;
  mediaErrorCode: number | null;
}

type DiagnosticSender = (diagnostic: AudioPlaybackDiagnosticRequest) => void;

/**
 * Attributes pause events to the Prismedia action that caused them and measures
 * browser media interruptions from their first signal until active playback resumes.
 */
export class AudioPlaybackDiagnosticReporter {
  private pendingPauseSource: AudioPlaybackPauseSourceCode | null = null;
  private interruptionStartedAt: number | null = null;

  constructor(
    private readonly send: DiagnosticSender,
    private readonly now: () => number = () => performance.now(),
  ) {}

  markPauseSource(source: AudioPlaybackPauseSourceCode): void {
    this.pendingPauseSource = source;
  }

  report(event: AudioPlaybackDiagnosticEventCode, snapshot: AudioPlaybackDiagnosticSnapshot): void {
    let pauseSource: AudioPlaybackPauseSourceCode | null = null;
    let interruptionMilliseconds: number | null = null;

    if (event === AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.pause) {
      pauseSource = this.pendingPauseSource ?? AUDIO_PLAYBACK_PAUSE_SOURCE.browser;
      this.pendingPauseSource = null;
      if (pauseSource === AUDIO_PLAYBACK_PAUSE_SOURCE.browser && snapshot.playIntent) {
        this.interruptionStartedAt ??= this.now();
      } else {
        this.interruptionStartedAt = null;
      }
    } else if (
      snapshot.playIntent &&
      (event === AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.waiting ||
        event === AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.stalled)
    ) {
      this.interruptionStartedAt ??= this.now();
    } else if (event === AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.playing) {
      this.pendingPauseSource = null;
      if (this.interruptionStartedAt !== null) {
        interruptionMilliseconds = Math.max(0, Math.round(this.now() - this.interruptionStartedAt));
        this.interruptionStartedAt = null;
      }
    } else if (event === AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.error) {
      this.pendingPauseSource = null;
      if (snapshot.playIntent) {
        this.interruptionStartedAt ??= this.now();
      } else {
        this.interruptionStartedAt = null;
      }
    }

    this.send({
      event,
      ...snapshot,
      pauseSource,
      interruptionMilliseconds,
    });
  }
}
