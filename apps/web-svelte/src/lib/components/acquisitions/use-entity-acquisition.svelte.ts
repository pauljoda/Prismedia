import { MONITOR_STATUS } from "$lib/api/generated/codes";
import type {
  AcquisitionDetail,
  EntityCapability,
  MonitorEligibilityView,
  MonitorView,
} from "$lib/api/generated/model";
import { firstExternalIdentity, isWanted } from "$lib/api/capabilities";
import { fetchAcquisitionForEntity } from "$lib/api/acquisitions";
import {
  fetchEntityMonitor,
  fetchMonitorEligibility,
  resumeMonitor,
  startEntityMonitor,
  stopMonitor,
} from "$lib/api/monitors";
import { commitEntityRequest, requestMissingChildren, syncContainerRequest } from "$lib/api/requests";
import { labelForEntityKind } from "$lib/entities/entity-codes";
import { resolveEntityThumbnailHref, type EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import {
  acquisitionStatusDisplay,
  type AcquisitionStatusDisplay,
} from "$lib/requests/acquisition-status-display";

/** One wanted/in-acquisition child of a container, mapped to a compact status row. */
export interface EntityAcquisitionChildStatus {
  id: string;
  title: string;
  kind: EntityThumbnailCard["entity"]["kind"];
  href: ReturnType<typeof resolveEntityThumbnailHref>;
  display: AcquisitionStatusDisplay;
}

export interface UseEntityAcquisitionOptions {
  entityId: () => string | null | undefined;
  capabilities?: () => EntityCapability[] | undefined;
  /**
   * The container's child thumbnails (seasons, books, albums). Any that are wanted/in-acquisition
   * are rolled up as a per-child status list, so a series' season states read from the top level
   * without opening each one. Owned children are ignored here (they show in the page's own grid).
   */
  childCards?: () => EntityThumbnailCard[] | undefined;
  /** Called after a state change (request started, monitor toggled, sync run) so the page can reload. */
  onChanged?: () => void | Promise<void>;
}

/**
 * The acquisition + monitoring state EntityAcquisitionCard renders. Headless and page-owned so the
 * page knows whether the entity has an acquisition story at all — `visible` gates the detail tab the
 * card lives in, which the card itself could never drive from inside a lazily-mounted tab panel.
 */
export interface EntityAcquisition {
  /** The entity's latest acquisition; writable because AcquisitionPanel binds its detail back up. */
  acquisition: AcquisitionDetail | null;
  readonly monitor: MonitorView | null;
  readonly monitorActive: boolean;
  /** Comma-joined provider ids the container monitor rides on (empty until eligibility loads). */
  readonly trackedVia: string;
  readonly showMonitor: boolean;
  readonly showSearch: boolean;
  readonly showSearchMissing: boolean;
  /** True when the entity has any acquisition story to show (drives the Acquisition tab). */
  readonly visible: boolean;
  readonly childStatuses: EntityAcquisitionChildStatus[];
  /** Plural kind label for the child roll-up heading (Seasons, Books, …). */
  readonly childKindLabel: string;
  readonly missingChildren: EntityAcquisitionChildStatus[];
  readonly monitorBusy: boolean;
  readonly syncBusy: boolean;
  readonly searchBusy: boolean;
  readonly missingBusy: boolean;
  /** Outcome line of the last "search missing" run ("Searching for 3 missing items…"), cleared after a beat. */
  readonly missingResult: string | null;
  /** Refresh acquisition + monitor state now (the poll re-reads the same slice). */
  refresh(): Promise<void>;
  toggleMonitor(): Promise<void>;
  syncNow(): Promise<void>;
  searchMissing(): Promise<void>;
  searchForRelease(): Promise<void>;
}

/**
 * Creates the headless acquisition/monitoring state for an entity page: the standing container
 * monitor (offered only when a metadata plugin can track one of the entity's provider identities),
 * the wanted placeholder's release search, the wanted-children roll-up, and the loaded acquisition.
 * Must be called during component init — it registers `$effect`s for loading and polling.
 */
export function useEntityAcquisition(options: UseEntityAcquisitionOptions): EntityAcquisition {
  let acquisition = $state<AcquisitionDetail | null>(null);
  let monitor = $state<MonitorView | null>(null);
  let eligibility = $state<MonitorEligibilityView | null>(null);
  let monitorBusy = $state(false);
  let syncBusy = $state(false);
  let searchBusy = $state(false);
  let missingBusy = $state(false);
  let missingResult = $state<string | null>(null);
  let loadedId = $state<string | null>(null);
  let lastRequestedId = "";

  // Child status roll-up: the wanted/in-acquisition children, each mapped to a compact status row.
  const childStatuses = $derived.by((): EntityAcquisitionChildStatus[] =>
    (options.childCards?.() ?? [])
      .filter((card) => isWanted(card.entity.capabilities))
      .map((card) => ({
        id: card.entity.id,
        title: card.entity.title,
        kind: card.entity.kind,
        href: resolveEntityThumbnailHref(card),
        display: acquisitionStatusDisplay(card.wantedStatus),
      })),
  );
  // The kind label is already plural (Seasons, Books, Audio Libraries), so it is used as-is.
  const childKindLabel = $derived(
    childStatuses.length > 0 ? labelForEntityKind(childStatuses[0].kind) : "",
  );
  // Children that are plain wanted phantoms with no acquisition — a season's missing episodes after a
  // pack import, or a series' unrequested seasons. These are what "Search missing" chases.
  const missingChildren = $derived(childStatuses.filter((child) => child.display.tone === "wanted"));

  const capabilities = $derived(options.capabilities?.());
  const wanted = $derived(!!capabilities && isWanted(capabilities));
  const monitorActive = $derived(monitor?.status === MONITOR_STATUS.active);
  const trackedVia = $derived(eligibility?.trackableProviders?.join(", ") ?? "");

  // The three blocks the card collapses. Container monitoring is offered only when the server says a
  // plugin can track the entity (or a monitor already exists and needs managing); the release search
  // only for a wanted phantom whose provider identity the server can resolve or degrade from.
  const showMonitor = $derived(monitor !== null || !!eligibility?.canMonitor);
  const showSearch = $derived(
    wanted && acquisition === null && !!capabilities && !!firstExternalIdentity(capabilities),
  );
  // "Search missing" requests each unrequested wanted descendant individually. Hidden while the
  // entity itself is a plain phantom — "Search for release" (the whole unit) is the primary action
  // there. Beyond the visible wanted-children roll-up, a MONITORED container always offers the
  // action: gaps can hide below the immediate children (a partially-owned season's missing episodes
  // under a series), which the server-side sweep reaches but the child cards cannot see.
  const showSearchMissing = $derived(
    !showSearch && (missingChildren.length > 0 || (monitorActive && options.childCards !== undefined)),
  );
  const visible = $derived(
    (loadedId !== null && (showMonitor || showSearch || acquisition !== null)) ||
      childStatuses.length > 0,
  );

  /**
   * Eligibility (can a plugin track this entity?) only changes when plugins or provider identities
   * change, so it loads once per entity; the poll below re-reads only the acquisition + monitor,
   * which are the states that actually move underneath the page.
   */
  async function refresh(refreshOptions: { includeEligibility?: boolean } = {}): Promise<void> {
    const id = options.entityId();
    if (!id) return;
    // On a transient fetch failure keep the last known state — nulling it would unmount the whole
    // panel mid-poll and reset its expanded/collapsed UI (a genuine "no acquisition" resolves null).
    const [nextAcquisition, nextMonitor, nextEligibility] = await Promise.all([
      fetchAcquisitionForEntity(id).catch(() => acquisition),
      fetchEntityMonitor(id).catch(() => monitor),
      refreshOptions.includeEligibility
        ? fetchMonitorEligibility(id).catch(() => null)
        : Promise.resolve(undefined),
    ]);
    // Ignore a stale load that resolved after the page moved to another entity.
    if (options.entityId() !== id) return;
    acquisition = nextAcquisition;
    monitor = nextMonitor;
    if (nextEligibility !== undefined) eligibility = nextEligibility;
    loadedId = id;
  }

  $effect(() => {
    const id = options.entityId();
    if (!id) {
      acquisition = null;
      monitor = null;
      eligibility = null;
      loadedId = null;
      lastRequestedId = "";
      return;
    }
    if (id === lastRequestedId) return;
    lastRequestedId = id;
    void refresh({ includeEligibility: true });
  });

  // Acquisition state changes outside this page too — the Downloads table re-searches, a monitor
  // sweep starts a grab for a phantom, a request commits from Discover. Poll while the entity has an
  // acquisition story (wanted, monitored, monitorable, or an acquisition in hand) so the card stays
  // lock-step with the global Downloads view; an ordinary owned entity never polls. AcquisitionPanel
  // separately polls the fine-grained transfer state while a download is live.
  $effect(() => {
    const shouldPoll =
      !!options.entityId() &&
      loadedId !== null &&
      (wanted || monitor !== null || !!eligibility?.canMonitor || acquisition !== null);
    if (!shouldPoll) return;
    const timer = setInterval(() => void refresh(), 5000);
    return () => clearInterval(timer);
  });

  /** Toggle semantics mirror the acquisition panel: not monitored → start; paused → resume; active → stop. */
  async function toggleMonitor(): Promise<void> {
    const id = options.entityId();
    if (!id || monitorBusy) return;
    monitorBusy = true;
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
      await options.onChanged?.();
    } catch {
      // best-effort; the card reflects the last known state
    } finally {
      monitorBusy = false;
    }
  }

  /** Run the discovery sync now instead of waiting for the daily sweep. */
  async function syncNow(): Promise<void> {
    const id = options.entityId();
    if (!id || syncBusy) return;
    syncBusy = true;
    try {
      await syncContainerRequest(id);
      await options.onChanged?.();
    } catch {
      // best-effort; the daily sweep covers it either way
    } finally {
      syncBusy = false;
    }
  }

  /** Requests each missing descendant (a season's absent episodes) as its own monitored acquisition. */
  async function searchMissing(): Promise<void> {
    const id = options.entityId();
    if (!id || missingBusy) return;
    missingBusy = true;
    missingResult = null;
    try {
      const outcome = await requestMissingChildren(id);
      missingResult =
        outcome.missing === 0
          ? "Nothing is missing right now."
          : `Searching for ${outcome.covered} of ${outcome.missing} missing item${outcome.missing === 1 ? "" : "s"}.`;
      setTimeout(() => (missingResult = null), 8000);
      await refresh();
      await options.onChanged?.();
    } catch {
      // best-effort; the card reflects the last known state
    } finally {
      missingBusy = false;
    }
  }

  /** Requests this phantom: starts its auto-grabbing, monitored acquisition and refreshes the page. */
  async function searchForRelease(): Promise<void> {
    const id = options.entityId();
    if (!id || searchBusy) return;
    searchBusy = true;
    try {
      await commitEntityRequest(id);
      await refresh();
      await options.onChanged?.();
    } catch {
      // best-effort; the page reflects the last known state
    } finally {
      searchBusy = false;
    }
  }

  return {
    get acquisition() {
      return acquisition;
    },
    set acquisition(value: AcquisitionDetail | null) {
      acquisition = value;
    },
    get monitor() {
      return monitor;
    },
    get monitorActive() {
      return monitorActive;
    },
    get trackedVia() {
      return trackedVia;
    },
    get showMonitor() {
      return showMonitor;
    },
    get showSearch() {
      return showSearch;
    },
    get showSearchMissing() {
      return showSearchMissing;
    },
    get visible() {
      return visible;
    },
    get childStatuses() {
      return childStatuses;
    },
    get childKindLabel() {
      return childKindLabel;
    },
    get missingChildren() {
      return missingChildren;
    },
    get monitorBusy() {
      return monitorBusy;
    },
    get syncBusy() {
      return syncBusy;
    },
    get searchBusy() {
      return searchBusy;
    },
    get missingBusy() {
      return missingBusy;
    },
    get missingResult() {
      return missingResult;
    },
    refresh: () => refresh(),
    toggleMonitor,
    syncNow,
    searchMissing,
    searchForRelease,
  };
}
