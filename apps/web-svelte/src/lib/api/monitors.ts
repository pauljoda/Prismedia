import {
  getEntityMonitor,
  getEntityMonitorEligibility,
  listCutoffUnmetWanted,
  listMissingWanted,
  listMonitors,
  pauseMonitor as pauseMonitorRequest,
  resumeMonitor as resumeMonitorRequest,
  startEntityMonitor as startEntityMonitorRequest,
  startMonitor as startMonitorRequest,
  stopMonitor as stopMonitorRequest,
} from "$lib/api/generated/prismedia";
import type { MonitorEligibilityView, MonitorView, WantedPageView } from "$lib/api/generated/model";
import type { MonitorPresetCode } from "$lib/api/generated/codes";
import { unwrapGenerated } from "$lib/api/generated-response";

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
 * Monitors a library container entity (an author, an artist, a series) for new works: the daily sweep
 * surfaces missing works as wanted placeholders under it. Requires the entity to carry a provider
 * identity (a scanned-in container gains one the moment Identify runs). An optional preset records the
 * monitoring scope (whether future syncs auto-monitor new works); omitting it keeps any preset a prior
 * request recorded, so a bare toggle never narrows the scope.
 */
export async function startEntityMonitor(entityId: string, preset?: MonitorPresetCode | null): Promise<MonitorView> {
  return unwrapGenerated(await startEntityMonitorRequest({ entityId, preset: preset ?? undefined }), "Failed to monitor this item");
}

/**
 * Whether the entity can carry a standing container monitor: it must be a monitorable container kind
 * holding a provider identity an enabled metadata plugin can track (re-resolve by id). The trackable
 * provider ids come along so the UI can name what the watch rides on.
 */
export async function fetchMonitorEligibility(entityId: string): Promise<MonitorEligibilityView> {
  return unwrapGenerated(await getEntityMonitorEligibility(entityId), "Failed to load monitoring eligibility");
}

/** The container monitor watching an entity, or null when it is not monitored. */
export async function fetchEntityMonitor(entityId: string): Promise<MonitorView | null> {
  const response = await getEntityMonitor(entityId);
  if (response.status === 404) return null;
  return unwrapGenerated(response, "Failed to load monitoring state");
}

export async function stopMonitor(monitorId: string): Promise<void> {
  unwrapGenerated(await stopMonitorRequest(monitorId), "Failed to stop monitoring", [204]);
}

export async function pauseMonitor(monitorId: string): Promise<void> {
  unwrapGenerated(await pauseMonitorRequest(monitorId), "Failed to pause monitor", [204]);
}

export async function resumeMonitor(monitorId: string): Promise<void> {
  unwrapGenerated(await resumeMonitorRequest(monitorId), "Failed to resume monitor", [204]);
}
