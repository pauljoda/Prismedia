import { MONITOR_STATUS } from "$lib/api/generated/codes";
import type { MonitorView } from "$lib/api/generated/model";

type MonitorStatusHolder = Pick<MonitorView, "status">;

/** True only while the monitor is actively searching or discovering content. */
export function monitorIsActive(monitor: MonitorStatusHolder | null): boolean {
  return monitor?.status === MONITOR_STATUS.active;
}

/** True only while managed Delete files owns the monitor; this is never an unmonitor retry. */
export function monitorIsDeletingFiles(monitor: MonitorStatusHolder | null): boolean {
  return MONITOR_STATUS.deletingFiles !== undefined
    && monitor?.status === MONITOR_STATUS.deletingFiles;
}

/** True only while recursive unmonitor cleanup owns the monitor and may be retried. */
export function monitorIsStopping(monitor: MonitorStatusHolder | null): boolean {
  return MONITOR_STATUS.stopping !== undefined
    && monitor?.status === MONITOR_STATUS.stopping;
}

/**
 * A status newer than this browser understands must never fall through to Resume or Stop. It keeps
 * the monitor visible but locks all destructive controls until generated contracts catch up.
 */
export function monitorHasUnknownStatus(monitor: MonitorStatusHolder | null): boolean {
  if (!monitor) return false;
  return monitor.status !== MONITOR_STATUS.active
    && monitor.status !== MONITOR_STATUS.paused
    && monitor.status !== MONITOR_STATUS.fulfilled
    && monitor.status !== MONITOR_STATUS.deletingFiles
    && monitor.status !== MONITOR_STATUS.stopping;
}

/** Any durable/unknown transition during which user monitor mutations must be disabled. */
export function monitorTransitionIsLocked(monitor: MonitorStatusHolder | null): boolean {
  return monitorIsDeletingFiles(monitor)
    || monitorIsStopping(monitor)
    || monitorHasUnknownStatus(monitor);
}
