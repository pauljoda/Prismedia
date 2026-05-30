import type { LedStatus } from "@prismedia/ui-svelte";
import type { WorkerHealthResponse } from "$lib/api/jobs";

export type WorkerHealthBadgeStatus = "checking" | "online" | "offline";

export interface WorkerHealthBadge {
  status: WorkerHealthBadgeStatus;
  label: string;
  tooltip: string;
  led: LedStatus;
  pulse: boolean;
}

export const workerOfflineTooltip =
  "Worker offline. It restarts automatically — this should clear on its own shortly.";

export function describeWorkerHealth(
  health: WorkerHealthResponse | null,
): WorkerHealthBadge {
  if (!health) {
    return {
      status: "checking",
      label: "Worker checking",
      tooltip: "Checking worker heartbeat.",
      led: "warning",
      pulse: true,
    };
  }

  if (health.status === "online") {
    return {
      status: "online",
      label: "Worker online",
      tooltip: lastSeenTooltip("Worker heartbeat is fresh.", health.lastSeenAt),
      led: "phosphor",
      pulse: false,
    };
  }

  return {
    status: "offline",
    label: "Worker offline",
    tooltip: lastSeenTooltip(workerOfflineTooltip, health.lastSeenAt),
    led: "error",
    pulse: false,
  };
}

function lastSeenTooltip(prefix: string, lastSeenAt: string | null): string {
  if (!lastSeenAt) return prefix;
  return `${prefix} Last seen ${new Date(lastSeenAt).toLocaleString()}.`;
}
