import { recordEntityPlaybackEvent, updateEntityPlayback } from "$lib/api/playback";
import { CONSUMPTION_EVENT_KIND } from "$lib/api/generated/codes";
import { ConsumptionActivityClock } from "$lib/entities/consumption-activity-clock";

interface MusicConsumptionSnapshot {
  positionSeconds: number;
  durationSeconds: number | null;
}

/** Records access and bounded active listening for the track loaded by the shared audio player. */
export class MusicConsumptionReporter {
  readonly #snapshot: () => MusicConsumptionSnapshot;
  readonly #now: () => number;
  #clock = new ConsumptionActivityClock();
  #trackId: string | null = null;
  #sessionId: string | null = null;
  #accessRecorded = false;

  constructor(
    snapshot: () => MusicConsumptionSnapshot,
    now: () => number = currentTimeMs,
  ) {
    this.#snapshot = snapshot;
    this.#now = now;
  }

  open(trackId: string): void {
    if (this.#trackId === trackId) return;
    this.close();
    this.#trackId = trackId;
    this.#sessionId = createConsumptionSessionId(trackId);
  }

  start(): void {
    if (!this.#trackId) return;
    this.#clock.start(this.#now());
    if (this.#accessRecorded) return;
    this.#accessRecorded = true;
    const { positionSeconds, durationSeconds } = this.#snapshot();
    void recordEntityPlaybackEvent(this.#trackId, {
      kind: CONSUMPTION_EVENT_KIND.accessed,
      positionSeconds,
      durationSeconds,
      sessionId: this.#sessionId,
    }).catch(() => undefined);
  }

  heartbeat(): void {
    this.#reportActivity(false);
  }

  pause(): void {
    this.#reportActivity(true);
  }

  close(): void {
    this.#reportActivity(true);
    this.#trackId = null;
    this.#sessionId = null;
    this.#accessRecorded = false;
    this.#clock = new ConsumptionActivityClock();
  }

  #reportActivity(stop: boolean): void {
    if (!this.#trackId) return;
    const now = this.#now();
    const durationSeconds = stop ? this.#clock.stop(now) : this.#clock.take(now);
    if (!durationSeconds) return;
    const { positionSeconds } = this.#snapshot();
    void updateEntityPlayback(this.#trackId, { resumeSeconds: positionSeconds, durationSeconds })
      .catch(() => undefined);
  }
}

export function createConsumptionSessionId(entityId: string): string {
  const suffix = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`;
  return `${entityId}:${suffix}`;
}

/** Records an audio owner opening without coupling the caller to the event payload. */
export function recordAudioConsumptionAccess(entityId: string, positionSeconds: number): void {
  void recordEntityPlaybackEvent(entityId, {
    kind: CONSUMPTION_EVENT_KIND.accessed,
    positionSeconds,
    sessionId: createConsumptionSessionId(entityId),
  }).catch(() => undefined);
}

function currentTimeMs(): number {
  return globalThis.performance?.now() ?? Date.now();
}
