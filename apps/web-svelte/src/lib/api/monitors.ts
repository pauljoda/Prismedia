import {
  getEntityMonitor,
  getEntityMonitorEligibility,
  getEntityMonitorStates,
  listCutoffUnmetWanted,
  listMissingWanted,
  listMonitors,
  pauseMonitor as pauseMonitorRequest,
  resumeMonitor as resumeMonitorRequest,
  startEntityMonitor as startEntityMonitorRequest,
  startMonitor as startMonitorRequest,
  stopMonitor as stopMonitorRequest,
} from "$lib/api/generated/prismedia";
import type {
  EntityMonitorStateView,
  MonitorEligibilityView,
  MonitorStopResponse,
  MonitorView,
  WantedPageView,
} from "$lib/api/generated/model";
import type { MonitorPresetCode } from "$lib/api/generated/codes";
import { unwrapGenerated } from "$lib/api/generated-response";

const ENTITY_MONITOR_STATE_BATCH_LIMIT = 500;

export async function fetchMonitors(): Promise<MonitorView[]> {
  return unwrapGenerated(await listMonitors(), "Failed to load monitors");
}

/** Query params shared by the two Wanted lists: 1-based page, clamped page size, optional kind filter. */
export interface WantedListParams {
  page?: number;
  pageSize?: number;
  /** An entity-kind code to filter to one kind, or undefined for all kinds. */
  kind?: string;
}

/**
 * A page of the Wanted "Missing" list: monitored items not yet acquired (an active monitor whose
 * acquisition is not imported), newest-monitor-first. The page total is exact.
 */
export async function fetchMissingWanted(params: WantedListParams = {}): Promise<WantedPageView> {
  return unwrapGenerated(await listMissingWanted(params), "Failed to load missing items");
}

/**
 * A page of the Wanted "Cutoff Unmet" list: monitored items in hand but still below their kind's quality
 * cutoff, newest-monitor-first. The page total is an upper bound (see the endpoint summary).
 */
export async function fetchCutoffUnmetWanted(params: WantedListParams = {}): Promise<WantedPageView> {
  return unwrapGenerated(await listCutoffUnmetWanted(params), "Failed to load cutoff-unmet items");
}

/** Starts (or re-activates) monitoring of an acquisition so its release search is re-run until it is acquired. */
export async function startMonitor(acquisitionId: string): Promise<MonitorView> {
  return unwrapGenerated(await startMonitorRequest({ acquisitionId }), "Failed to start monitoring");
}

/**
 * Starts stable monitoring for a requestable Entity through its authoritative plugin identity. Grouping
 * Entities discover children; leaves keep their acquisition/presence intent attached to the Entity even
 * when transient acquisition rows are replaced. A preset only affects grouping discovery scope.
 */
export async function startEntityMonitor(entityId: string, preset?: MonitorPresetCode | null): Promise<MonitorView> {
  return unwrapGenerated(await startEntityMonitorRequest({ entityId, preset: preset ?? undefined }), "Failed to monitor this item");
}

/** Whether this requestable Entity's authoritative provider identity is trackable by an enabled plugin. */
export async function fetchMonitorEligibility(entityId: string): Promise<MonitorEligibilityView> {
  return unwrapGenerated(await getEntityMonitorEligibility(entityId), "Failed to load monitoring eligibility");
}

/**
 * Loads the bounded monitoring read model for a set of direct child Entities. The server resolves
 * plugin eligibility, direct monitor intent, and latest acquisition state in batch, so a child panel
 * never downloads and filters the application's global acquisition/monitor lists.
 */
export async function fetchEntityMonitorStates(entityIds: string[]): Promise<EntityMonitorStateView[]> {
  const uniqueIds = [...new Set(entityIds)];
  if (uniqueIds.length === 0) return [];

  const batches: string[][] = [];
  for (let offset = 0; offset < uniqueIds.length; offset += ENTITY_MONITOR_STATE_BATCH_LIMIT) {
    batches.push(uniqueIds.slice(offset, offset + ENTITY_MONITOR_STATE_BATCH_LIMIT));
  }
  const responses = await Promise.all(
    batches.map(async (batch) =>
      unwrapGenerated<EntityMonitorStateView[]>(
        await getEntityMonitorStates({ entityIds: batch }),
        "Failed to load child monitoring",
      )),
  );
  return responses.flat();
}

/** The stable monitor targeting an Entity, or null when it is not monitored. */
export async function fetchEntityMonitor(entityId: string): Promise<MonitorView | null> {
  const response = await getEntityMonitor(entityId);
  if (response.status === 404) return null;
  return unwrapGenerated(response, "Failed to load monitoring state");
}

export async function stopMonitor(monitorId: string): Promise<MonitorStopResponse> {
  return unwrapGenerated(await stopMonitorRequest(monitorId), "Failed to stop monitoring", [200]);
}

export async function pauseMonitor(monitorId: string): Promise<void> {
  unwrapGenerated(await pauseMonitorRequest(monitorId), "Failed to pause monitor", [204]);
}

export async function resumeMonitor(monitorId: string): Promise<void> {
  unwrapGenerated(await resumeMonitorRequest(monitorId), "Failed to resume monitor", [204]);
}
