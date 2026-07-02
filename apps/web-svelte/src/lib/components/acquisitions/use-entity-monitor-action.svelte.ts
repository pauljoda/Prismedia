import { Bell, BellRing } from "@lucide/svelte";
import { MONITOR_STATUS } from "$lib/api/generated/codes";
import type { EntityCapability, MonitorView } from "$lib/api/generated/model";
import { firstProviderQualifiedId } from "$lib/api/capabilities";
import { fetchEntityMonitor, resumeMonitor, startEntityMonitor, stopMonitor } from "$lib/api/monitors";
import type { EntityDetailActionButton } from "$lib/components/entities/EntityDetail.svelte";

/**
 * Creates a headless EntityDetail hero action that monitors a library container entity (an author, an
 * artist) for new works. The same control serves wanted placeholders and real scanned-in entities —
 * merging the on-disk and requested flows is the point. The action only appears once the entity carries
 * a provider identity (run Identify first for scanned-in items), because that identity is what the
 * daily discovery sync re-resolves the container from. Toggle semantics mirror the acquisition panel:
 * not monitored → start; paused → resume; actively monitoring → stop.
 */
export function useEntityMonitorAction(
  entityId: () => string | null | undefined,
  capabilities: () => EntityCapability[] | undefined,
): { readonly action: EntityDetailActionButton | null } {
  let monitor: MonitorView | null = $state(null);
  let busy = $state(false);
  let lastLoadedId = "";

  const providerId = $derived.by(() => {
    const caps = capabilities();
    return caps ? firstProviderQualifiedId(caps) : null;
  });
  const monitorActive = $derived.by(() => monitor?.status === MONITOR_STATUS.active);

  $effect(() => {
    const id = entityId();
    if (!id) {
      monitor = null;
      lastLoadedId = "";
      return;
    }

    if (id === lastLoadedId) return;
    lastLoadedId = id;
    fetchEntityMonitor(id).then(
      (loaded) => (monitor = loaded),
      () => (monitor = null),
    );
  });

  async function toggle() {
    const id = entityId();
    if (!id || busy) return;
    busy = true;
    try {
      if (monitor && monitorActive) {
        await stopMonitor(monitor.id);
        monitor = null;
      } else if (monitor) {
        await resumeMonitor(monitor.id);
        monitor = { ...monitor, status: MONITOR_STATUS.active };
      } else {
        monitor = await startEntityMonitor(id);
      }
    } catch {
      // best-effort; the button reflects the last known state
    } finally {
      busy = false;
    }
  }

  const action = $derived.by((): EntityDetailActionButton | null => {
    const id = entityId();
    // No provider identity yet → nothing to monitor from; the Identify action beside this one is the
    // way to get one, so the control stays out of the way instead of rendering dead.
    if (!id || (!providerId && !monitor)) return null;

    const label = monitorActive ? "Monitoring" : monitor ? "Resume monitoring" : "Monitor";
    return {
      id: "entity-monitor",
      label,
      icon: monitorActive ? BellRing : Bell,
      iconClass: "h-3.5 w-3.5",
      title: monitorActive
        ? "Watching for new works daily — click to stop"
        : "Watch this for new works; they appear as Wanted placeholders",
      ariaLabel: label,
      active: monitorActive,
      disabled: busy,
      onClick: () => void toggle(),
    };
  });

  return {
    get action() {
      return action;
    },
  };
}
