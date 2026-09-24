import type {
  ConsumptionActivityKindCode,
  ConsumptionEventKindCode,
  ConsumptionModalityCode,
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

/**
 * Reports progress. Reading reports carry the cursor and exact locator; listening reports carry the
 * modality and exact track position, and the server places the shared cursor.
 */
export async function updateEntityProgress(
  id: string,
  payload: {
    currentEntityId?: string | null;
    unit?: ProgressUnitCode | null;
    index?: number | null;
    total?: number | null;
    mode?: ReaderModeCode | null;
    completed?: boolean | null;
    reset?: boolean;
    location?: string | null;
    activitySeconds?: number | null;
    activityKind?: ConsumptionActivityKindCode;
    utcOffsetMinutes?: number | null;
    modality?: ConsumptionModalityCode | null;
    listening?: EntityProgressUpdateRequest["listening"];
  },
  options?: RequestOptions,
): Promise<void> {
  unwrapGenerated<void>(
    await updateEntityProgressRequest(
      id,
      {
        currentEntityId: payload.currentEntityId ?? null,
        unit: payload.unit ?? null,
        index: payload.index ?? null,
        total: payload.total ?? null,
        mode: payload.mode ?? null,
        completed: payload.completed ?? null,
        reset: payload.reset ?? false,
        location: payload.location ?? null,
        activitySeconds: payload.activitySeconds ?? null,
        activityKind: payload.activityKind,
        utcOffsetMinutes: payload.utcOffsetMinutes ?? localUtcOffsetMinutes(),
        modality: payload.modality ?? null,
        listening: payload.listening ?? null,
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
