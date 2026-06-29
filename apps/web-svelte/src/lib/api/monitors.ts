import {
  listMonitors,
  pauseMonitor as pauseMonitorRequest,
  resumeMonitor as resumeMonitorRequest,
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

export async function stopMonitor(monitorId: string): Promise<void> {
  unwrapGenerated(await stopMonitorRequest(monitorId), "Failed to stop monitoring", [204]);
}

export async function pauseMonitor(monitorId: string): Promise<void> {
  unwrapGenerated(await pauseMonitorRequest(monitorId), "Failed to pause monitor", [204]);
}

export async function resumeMonitor(monitorId: string): Promise<void> {
  unwrapGenerated(await resumeMonitorRequest(monitorId), "Failed to resume monitor", [204]);
}
