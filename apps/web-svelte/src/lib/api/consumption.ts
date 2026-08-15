import type {
  ConsumptionActivityKindCode,
  ConsumptionEventKindCode,
  ProgressUnitCode,
  ReaderModeCode,
} from "$lib/api/generated/codes";
import {
  createEntityConsumptionEvent as createEntityConsumptionEventRequest,
  updateEntityConsumption as updateEntityConsumptionRequest,
  updateEntityProgress as updateEntityProgressRequest,
} from "$lib/api/generated/prismedia";
import type {
  ConsumptionEventCreateRequest,
  ConsumptionUpdateRequest,
  EntityCard,
  EntityProgressUpdateRequest,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";

/** Updates the shared time position and adds bounded active consumption time. */
export async function updateEntityConsumption(
  id: string,
  payload: {
    positionSeconds?: number | null;
    activitySeconds?: number | null;
    completed?: boolean | null;
    utcOffsetMinutes?: number | null;
  },
  options?: RequestOptions,
): Promise<EntityCard> {
  return unwrapGenerated(
    await updateEntityConsumptionRequest(
      id,
      {
        positionSeconds: payload.positionSeconds ?? null,
        activitySeconds: payload.activitySeconds ?? null,
        completed: payload.completed ?? null,
        utcOffsetMinutes: payload.utcOffsetMinutes ?? localUtcOffsetMinutes(),
      } as ConsumptionUpdateRequest,
      requestInit(options),
    ),
    `Failed to update consumption for ${id}`,
  );
}

/** Updates the canonical last-active progress cursor and optional exact locator. */
export async function updateEntityProgress(
  id: string,
  payload: {
    currentEntityId: string;
    unit: ProgressUnitCode;
    index: number;
    total: number;
    mode?: ReaderModeCode | null;
    completed?: boolean | null;
    reset?: boolean;
    location?: string | null;
    activitySeconds?: number | null;
    activityKind?: ConsumptionActivityKindCode;
    utcOffsetMinutes?: number | null;
  },
  options?: RequestOptions,
): Promise<void> {
  unwrapGenerated<void>(
    await updateEntityProgressRequest(
      id,
      {
        currentEntityId: payload.currentEntityId,
        unit: payload.unit,
        index: payload.index,
        total: payload.total,
        mode: payload.mode ?? null,
        completed: payload.completed ?? null,
        reset: payload.reset ?? false,
        location: payload.location ?? null,
        activitySeconds: payload.activitySeconds ?? null,
        activityKind: payload.activityKind,
        utcOffsetMinutes: payload.utcOffsetMinutes ?? localUtcOffsetMinutes(),
      } as EntityProgressUpdateRequest,
      {
        ...requestInit(options),
        headers: { Prefer: "return=minimal" },
      },
    ),
    `Failed to update progress for ${id}`,
    [204],
  );
}

/** Appends one timestamped access, completion, or skip event. */
export async function recordEntityConsumptionEvent(
  id: string,
  payload: {
    kind: ConsumptionEventKindCode;
    occurredAt?: string | null;
    positionSeconds?: number | null;
    durationSeconds?: number | null;
    sessionId?: string | null;
  },
  options?: RequestOptions,
): Promise<EntityCard> {
  return unwrapGenerated(
    await createEntityConsumptionEventRequest(
      id,
      {
        kind: payload.kind,
        occurredAt: payload.occurredAt ?? null,
        positionSeconds: payload.positionSeconds ?? null,
        durationSeconds: payload.durationSeconds ?? null,
        sessionId: payload.sessionId ?? null,
      } as ConsumptionEventCreateRequest,
      requestInit(options),
    ),
    `Failed to record consumption event for ${id}`,
  );
}

function localUtcOffsetMinutes(): number {
  return typeof Date === "undefined" ? 0 : -new Date().getTimezoneOffset();
}
