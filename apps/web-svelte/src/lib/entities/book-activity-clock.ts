/** Tracks active reading/listening time between progress heartbeats. */
export class BookActivityClock {
  readonly #maxSeconds: number;
  #startedAtMs: number | null = null;

  constructor(maxSeconds = 60) {
    this.#maxSeconds = Math.max(0, maxSeconds);
  }

  start(nowMs = currentTimeMs()): void {
    this.#startedAtMs ??= nowMs;
  }

  take(nowMs = currentTimeMs()): number | null {
    if (this.#startedAtMs === null) return null;
    const seconds = Math.min(this.#maxSeconds, Math.max(0, (nowMs - this.#startedAtMs) / 1_000));
    this.#startedAtMs = nowMs;
    return seconds > 0 ? seconds : null;
  }

  stop(nowMs = currentTimeMs()): number | null {
    const seconds = this.take(nowMs);
    this.#startedAtMs = null;
    return seconds;
  }
}

function currentTimeMs(): number {
  return globalThis.performance?.now() ?? Date.now();
}
