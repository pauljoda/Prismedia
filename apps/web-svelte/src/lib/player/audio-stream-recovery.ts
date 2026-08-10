export interface AudioStreamInterruption {
  trackId: string;
  positionSeconds: number;
}

type RecoveryCallback = (interruption: AudioStreamInterruption) => void;

const FIRST_RECOVERY_DELAY_MS = 4_000;
const MAX_RECOVERY_DELAY_MS = 15_000;
const TERMINAL_ERROR_RECOVERY_DELAY_MS = 250;
const STABLE_PLAYBACK_RESET_MS = 30_000;

/**
 * Coalesces browser media interruption signals and applies bounded backoff when
 * a replacement audio request also struggles. A sustained playing interval
 * resets the backoff for the next unrelated network blip.
 */
export class AudioStreamRecoveryController {
  private pendingTimer: ReturnType<typeof setTimeout> | null = null;
  private stablePlaybackTimer: ReturnType<typeof setTimeout> | null = null;
  private recoveryAttempt = 0;

  constructor(private readonly recover: RecoveryCallback) {}

  /** Schedules one replacement request for a waiting, stalled, or media-error signal. */
  interrupt(interruption: AudioStreamInterruption, terminalError = false): void {
    if (this.pendingTimer !== null) return;
    this.clearStablePlaybackTimer();
    const delay = terminalError && this.recoveryAttempt === 0
      ? TERMINAL_ERROR_RECOVERY_DELAY_MS
      : Math.min(FIRST_RECOVERY_DELAY_MS * 2 ** this.recoveryAttempt, MAX_RECOVERY_DELAY_MS);
    this.pendingTimer = setTimeout(() => {
      this.pendingTimer = null;
      this.recoveryAttempt += 1;
      this.recover(interruption);
    }, delay);
  }

  /** Cancels a pending replacement and resets backoff after playback remains healthy. */
  playing(): void {
    this.clearPendingTimer();
    this.clearStablePlaybackTimer();
    this.stablePlaybackTimer = setTimeout(() => {
      this.stablePlaybackTimer = null;
      this.recoveryAttempt = 0;
    }, STABLE_PLAYBACK_RESET_MS);
  }

  /** Clears all state when the user pauses, the track changes, or the player unmounts. */
  reset(): void {
    this.clearPendingTimer();
    this.clearStablePlaybackTimer();
    this.recoveryAttempt = 0;
  }

  private clearPendingTimer(): void {
    if (this.pendingTimer === null) return;
    clearTimeout(this.pendingTimer);
    this.pendingTimer = null;
  }

  private clearStablePlaybackTimer(): void {
    if (this.stablePlaybackTimer === null) return;
    clearTimeout(this.stablePlaybackTimer);
    this.stablePlaybackTimer = null;
  }
}
