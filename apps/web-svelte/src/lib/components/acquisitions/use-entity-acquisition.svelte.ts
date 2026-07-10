import { MONITOR_STATUS } from "$lib/api/generated/codes";
import type {
  AcquisitionDetail,
  EntityCapability,
  MonitorEligibilityView,
  MonitorView,
} from "$lib/api/generated/model";
import { canDeleteEntityFiles, firstExternalIdentity, isWanted } from "$lib/api/capabilities";
import { fetchAcquisitionForEntity } from "$lib/api/acquisitions";
import {
  fetchEntityMonitor,
  fetchMonitorEligibility,
  resumeMonitor,
  startEntityMonitor,
  stopMonitor,
} from "$lib/api/monitors";
import { commitEntityRequest, requestMissingChildren, syncContainerRequest } from "$lib/api/requests";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import { acquisitionStatusShouldPoll } from "$lib/requests/acquisition-status";
import { acquisitionStatusDisplay } from "$lib/requests/acquisition-status-display";
import {
  monitorHasUnknownStatus,
  monitorIsActive,
  monitorIsDeletingFiles,
  monitorIsStopping,
  monitorTransitionIsLocked,
} from "$lib/requests/monitor-status";

export interface UseEntityAcquisitionOptions {
  entityId: () => string | null | undefined;
  capabilities?: () => EntityCapability[] | undefined;
  /**
   * The Entity's direct child cards (seasons, books, albums). The shared acquisition
   * surface uses the same cards for per-child monitoring and missing-child search state.
   */
  childCards?: () => EntityThumbnailCard[] | undefined;
  /** Called after a state change (request started, monitor toggled, sync run) so the page can reload. */
  onChanged?: () => void | Promise<void>;
  /** Called instead of refreshing when unmonitoring pruned the fileless Entity backing this detail route. */
  onPruned?: () => void | Promise<void>;
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
  /** Durable server-side cleanup is in progress; it may only retry stop, never resume. */
  readonly monitorStopping: boolean;
  /** Managed file deletion owns the monitor; intent stays on but every monitor action is locked. */
  readonly monitorDeletingFiles: boolean;
  /** The browser does not understand the status yet, so every monitor action fails closed. */
  readonly monitorUnknownStatus: boolean;
  /** Comma-joined plugin ids the stable monitor rides on (empty until eligibility loads). */
  readonly trackedVia: string;
  readonly showMonitor: boolean;
  /** Provider discovery is meaningful only for grouping entities, never a monitored leaf. */
  readonly showSync: boolean;
  readonly showSearch: boolean;
  readonly showSearchMissing: boolean;
  /** The Entity owns managed on-disk files that can be removed from this Acquisition surface. */
  readonly showFileManagement: boolean;
  /** True when the entity has any acquisition story to show (drives the Acquisition tab). */
  readonly visible: boolean;
  /** Every direct child Entity supplied by the page, used by the shared child-monitoring editor. */
  readonly childCards: EntityThumbnailCard[];
  readonly missingChildCount: number;
  readonly monitorBusy: boolean;
  /** Actionable failure from the most recent monitor start/resume/stop attempt. */
  readonly monitorError: string | null;
  readonly syncBusy: boolean;
  readonly searchBusy: boolean;
  readonly missingBusy: boolean;
  /** Outcome line of the last "search missing" run ("Searching for 3 missing items…"), cleared after a beat. */
  readonly missingResult: string | null;
  /** Refresh acquisition + monitor state now (the poll re-reads the same slice). */
  refresh(): Promise<void>;
  /** Immediately forgets an acquisition that the server deleted, unmounting every stale-id surface. */
  clearAcquisition(): void;
  toggleMonitor(): Promise<void>;
  syncNow(): Promise<void>;
  searchMissing(): Promise<void>;
  searchForRelease(): Promise<void>;
  /** Refreshes the owning page after a child monitor creates or removes wanted descendants. */
  childMonitoringChanged(): Promise<void>;
}

/**
 * Creates the headless acquisition/monitoring state for an entity page: the stable Entity monitor
 * (offered only when a metadata plugin can track the authoritative provider identity),
 * the wanted placeholder's release search, the wanted-children roll-up, and the loaded acquisition.
 * Must be called during component init — it registers `$effect`s for loading and polling.
 */
export function useEntityAcquisition(options: UseEntityAcquisitionOptions): EntityAcquisition {
  let acquisition = $state<AcquisitionDetail | null>(null);
  let monitor = $state<MonitorView | null>(null);
  let eligibility = $state<MonitorEligibilityView | null>(null);
  let monitorBusy = $state(false);
  let monitorError = $state<string | null>(null);
  let syncBusy = $state(false);
  let searchBusy = $state(false);
  let missingBusy = $state(false);
  let missingResult = $state<string | null>(null);
  let loadedId = $state<string | null>(null);
  let lastRequestedId = "";

  const childCards = $derived(options.childCards?.() ?? []);
  const hasActiveChildAcquisition = $derived(
    childCards.some((card) =>
      acquisitionStatusShouldPoll(card.wantedStatus)
      || acquisitionStatusShouldPoll(card.latestAcquisitionStatus),
    ),
  );
  // Only the server-declared request child kind contributes to the visible gap count. Mixed direct
  // children remain independently monitorable, but a series' loose videos/sub-series cannot inflate its
  // season count and an album with child discs cannot invent a missing-search capability.
  const missingChildCount = $derived(
    childCards.filter(
      (card) => card.entity.kind === eligibility?.missingChildEntityKind
        && isWanted(card.entity.capabilities) &&
        acquisitionStatusDisplay(card.wantedStatus).tone === "wanted",
    ).length,
  );

  const capabilities = $derived(options.capabilities?.());
  const wanted = $derived(!!capabilities && isWanted(capabilities));
  const monitorActive = $derived(monitorIsActive(monitor));
  const monitorStopping = $derived(monitorIsStopping(monitor));
  const monitorDeletingFiles = $derived(monitorIsDeletingFiles(monitor));
  const monitorUnknownStatus = $derived(monitorHasUnknownStatus(monitor));
  const monitorTransitionLocked = $derived(monitorTransitionIsLocked(monitor));
  const trackedVia = $derived(eligibility?.trackableProviders?.join(", ") ?? "");

  // The three blocks the card collapses. Monitoring is offered only when the server says a plugin can
  // track the Entity (or a monitor already exists and needs managing); the release search
  // only for a wanted phantom whose provider identity the server can resolve or degrade from.
  const showMonitor = $derived(monitor !== null || !!eligibility?.canMonitor);
  const showSync = $derived(monitorActive && eligibility?.discoversChildren === true);
  const showSearch = $derived(
    !monitorTransitionLocked
      && wanted
      && acquisition === null
      && !!capabilities
      && !!firstExternalIdentity(capabilities),
  );
  // "Search missing" is a request-registry capability authored by the server, not inferred from whether
  // a route happened to supply cards. Hidden while the entity itself is a plain phantom — "Search for
  // release" is primary there. A monitored capable parent keeps the action even with no visible direct
  // gaps because deeper owned children can still contain missing descendants.
  const showSearchMissing = $derived(
    !monitorTransitionLocked
      && !showSearch
      && eligibility?.canSearchMissingChildren === true
      && (missingChildCount > 0 || monitorActive),
  );
  const showFileManagement = $derived(
    Boolean(capabilities && canDeleteEntityFiles(capabilities)),
  );
  const visible = $derived(
    (loadedId !== null && (showMonitor || showSearch || showFileManagement || acquisition !== null)) ||
      childCards.length > 0,
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
      monitorError = null;
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
      (wanted || monitor !== null || !!eligibility?.canMonitor || acquisition !== null || hasActiveChildAcquisition);
    if (!shouldPoll) return;
    let pollBusy = false;
    const poll = async () => {
      if (pollBusy) return;
      pollBusy = true;
      try {
        await refresh();
        // Child monitor/acquisition state lives in the owning page's Entity cards. Refresh that
        // read model even when the child editor is collapsed so Imported becomes ready immediately.
        if (hasActiveChildAcquisition) await options.onChanged?.();
      } catch {
        // Polling is best-effort; the next interval retries both slices.
      } finally {
        pollBusy = false;
      }
    };
    const timer = setInterval(() => void poll(), 5000);
    return () => clearInterval(timer);
  });

  /** The shared Entity-level monitor control: not monitored → start; paused → resume; active → stop. */
  async function toggleMonitor(): Promise<void> {
    const id = options.entityId();
    if (!id || monitorBusy || monitorDeletingFiles || monitorUnknownStatus) return;
    monitorBusy = true;
    monitorError = null;
    let ownerFollowUp = options.onChanged;
    try {
      if (monitor && (monitorActive || monitorStopping)) {
        const outcome = await stopMonitor(monitor.id);
        monitor = null;
        // Stop tears down pending acquisition state. Unmount its panel in this same SPA tick before
        // any owner reload can leave a deleted acquisition id interactive.
        acquisition = null;
        if (outcome.entityPruned) {
          ownerFollowUp = options.onPruned;
        } else {
          await refresh();
        }
      } else if (monitor) {
        await resumeMonitor(monitor.id);
        monitor = { ...monitor, status: MONITOR_STATUS.active };
      } else {
        monitor = await startEntityMonitor(id);
      }
    } catch (reason) {
      // Preserve the last confirmed state and tell the user why the requested transition did not land.
      monitorError = reason instanceof Error ? reason.message : "Failed to update monitoring";
      monitorBusy = false;
      return;
    }

    try {
      await ownerFollowUp?.();
    } catch (reason) {
      const detail = reason instanceof Error ? reason.message : "Unknown refresh error";
      monitorError = `Monitoring was updated, but this page could not refresh: ${detail}`;
    } finally {
      monitorBusy = false;
    }
  }

  /** Run the discovery sync now instead of waiting for the daily sweep. */
  async function syncNow(): Promise<void> {
    const id = options.entityId();
    if (!id || !showSync || syncBusy) return;
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

  /** Requests this phantom and refreshes only its acquisition slice; the Entity itself did not change. */
  async function searchForRelease(): Promise<void> {
    const id = options.entityId();
    if (!id || searchBusy) return;
    searchBusy = true;
    try {
      await commitEntityRequest(id);
      await refresh();
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
    get monitorStopping() {
      return monitorStopping;
    },
    get monitorDeletingFiles() {
      return monitorDeletingFiles;
    },
    get monitorUnknownStatus() {
      return monitorUnknownStatus;
    },
    get trackedVia() {
      return trackedVia;
    },
    get showMonitor() {
      return showMonitor;
    },
    get showSync() {
      return showSync;
    },
    get showSearch() {
      return showSearch;
    },
    get showSearchMissing() {
      return showSearchMissing;
    },
    get showFileManagement() {
      return showFileManagement;
    },
    get visible() {
      return visible;
    },
    get childCards() {
      return childCards;
    },
    get missingChildCount() {
      return missingChildCount;
    },
    get monitorBusy() {
      return monitorBusy;
    },
    get monitorError() {
      return monitorError;
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
    clearAcquisition: () => {
      acquisition = null;
    },
    toggleMonitor,
    syncNow,
    searchMissing,
    searchForRelease,
    childMonitoringChanged: async () => {
      await options.onChanged?.();
    },
  };
}
