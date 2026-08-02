import { recordEntityPlaybackEvent, updateEntityPlayback } from "$lib/api/playback";
import { CONSUMPTION_EVENT_KIND } from "$lib/api/generated/codes";
import { ConsumptionActivityClock } from "$lib/entities/consumption-activity-clock";

export interface EntityViewingSink {
  recordAccess(entityId: string, sessionId: string): Promise<unknown>;
  recordActivity(entityId: string, seconds: number): Promise<unknown>;
}

/** Owns one active image/gallery viewing session and its bounded heartbeats. */
export class EntityViewingSession {
  readonly #sink: EntityViewingSink;
  readonly #now: () => number;
  #clock = new ConsumptionActivityClock();
  #entityId: string | null = null;
  #pendingReport: Promise<void> = Promise.resolve();

  constructor(
    sink: EntityViewingSink = apiViewingSink,
    now: () => number = currentTimeMs,
  ) {
    this.#sink = sink;
    this.#now = now;
  }

  open(entityId: string, active = true): void {
    if (this.#entityId === entityId) {
      if (active) this.resume();
      return;
    }
    this.#flush(true);
    this.#entityId = entityId;
    this.#clock = new ConsumptionActivityClock();
    const sessionId = createSessionId();
    this.#enqueue(() => this.#sink.recordAccess(entityId, sessionId));
    if (active) this.#clock.start(this.#now());
  }

  resume(): void {
    if (this.#entityId) this.#clock.start(this.#now());
  }

  heartbeat(): void {
    this.#flush(false);
  }

  pause(): void {
    this.#flush(true);
  }

  close(): void {
    this.#flush(true);
    this.#entityId = null;
    this.#clock = new ConsumptionActivityClock();
  }

  /** Waits until the access and active-time reports queued so far have settled. */
  flush(): Promise<void> {
    return this.#pendingReport;
  }

  #flush(stop: boolean): void {
    if (!this.#entityId) return;
    const seconds = stop ? this.#clock.stop(this.#now()) : this.#clock.take(this.#now());
    if (!seconds) return;
    const entityId = this.#entityId;
    this.#enqueue(() => this.#sink.recordActivity(entityId, seconds));
  }

  #enqueue(operation: () => Promise<unknown>): void {
    this.#pendingReport = this.#pendingReport.then(operation, operation).then(
      () => undefined,
      () => undefined,
    );
  }
}

const apiViewingSink: EntityViewingSink = {
  recordAccess: (entityId, sessionId) =>
    recordEntityPlaybackEvent(entityId, {
      kind: CONSUMPTION_EVENT_KIND.accessed,
      sessionId,
    }),
  recordActivity: (entityId, seconds) =>
    updateEntityPlayback(entityId, { durationSeconds: seconds }),
};

function createSessionId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `view-${Date.now()}-${Math.random()}`;
}

function currentTimeMs(): number {
  return globalThis.performance?.now() ?? Date.now();
}
