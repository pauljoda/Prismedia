import {
  getEntityMonitor,
  listMonitors,
  pauseMonitor as pauseMonitorRequest,
  resumeMonitor as resumeMonitorRequest,
  startEntityMonitor as startEntityMonitorRequest,
  startMonitor as startMonitorRequest,
  stopMonitor as stopMonitorRequest,
} from "$lib/api/generated/prismedia";
import type { MonitorView } from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";

export async function fetchMonitors(): Promise<MonitorView[]> {
  return unwrapGenerated(await listMonitors(), "Failed to load monitors");
}

/** Starts (or re-activates) monitoring of an acquisition so its release search is re-run until it is acquired. */
export async function startMonitor(acquisitionId: string): Promise<MonitorView> {
  return unwrapGenerated(await startMonitorRequest({ acquisitionId }), "Failed to start monitoring");
}

/**
 * Monitors a library container entity (an author, an artist) for new works: the daily sweep surfaces
 * missing works as wanted placeholders under it. Requires the entity to carry a provider identity
 * (a scanned-in container gains one the moment Identify runs).
 */
export async function startEntityMonitor(entityId: string): Promise<MonitorView> {
  return unwrapGenerated(await startEntityMonitorRequest({ entityId }), "Failed to monitor this item");
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
