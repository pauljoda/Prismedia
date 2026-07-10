<script lang="ts">
  import { ChevronDown, Layers, Loader2 } from "@lucide/svelte";
  import { Button, Toggle, cn } from "@prismedia/ui-svelte";
  import { ACQUISITION_STATUS } from "$lib/api/generated/codes";
  import type {
    AcquisitionSummary,
    MonitorView,
  } from "$lib/api/generated/model";
  import { isWanted } from "$lib/api/capabilities";
  import {
    fetchEntityMonitorStates,
    resumeMonitor,
    startEntityMonitor,
    stopMonitor,
  } from "$lib/api/monitors";
  import { commitEntityRequest } from "$lib/api/requests";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import {
    resolveEntityThumbnailHref,
    type EntityThumbnailCard,
  } from "$lib/entities/entity-thumbnail";
  import { acquisitionStatusDisplay } from "$lib/requests/acquisition-status-display";
  import { ACTIVE_ACQUISITION_STATUSES } from "$lib/requests/acquisition-status";
  import {
    monitorHasUnknownStatus,
    monitorIsActive,
    monitorIsDeletingFiles,
    monitorIsStopping,
  } from "$lib/requests/monitor-status";

  interface ChildMonitoringRow {
    card: EntityThumbnailCard;
    acquisition: AcquisitionSummary | null;
    monitor: MonitorView | null;
    canMonitor: boolean;
    canRequest: boolean;
  }

  interface RefreshOptions {
    showLoading?: boolean;
    waitForCurrent?: boolean;
    reportErrors?: boolean;
  }

  const POLL_INTERVAL_MS = 5_000;

  let {
    cards,
    onChanged,
  }: {
    /** Direct child Entities managed by this parent, regardless of medium. */
    cards: EntityThumbnailCard[];
    /** Refreshes the owning Entity graph after monitoring creates or removes wanted children. */
    onChanged?: () => void | Promise<void>;
  } = $props();

  let open = $state(false);
  let loading = $state(false);
  let bulkBusy = $state(false);
  let busyIds = $state.raw<string[]>([]);
  let rows = $state.raw<ChildMonitoringRow[]>([]);
  let error = $state<string | null>(null);
  let outcome = $state<string | null>(null);
  let refreshInFlight: Promise<void> | null = null;

  const cardsKey = $derived(cards.map((card) => card.entity.id).join("|"));
  const childLabel = $derived.by(() => {
    const kinds = new Set(cards.map((card) => card.entity.kind));
    return kinds.size === 1 && cards[0]
      ? labelForEntityKind(cards[0].entity.kind)
      : "Items";
  });
  const actionableRows = $derived(rows.filter(canSetMonitoring));
  const acting = $derived(bulkBusy || busyIds.length > 0);

  // The open panel is a live read surface: loading on every expansion prevents a same-id cache from
  // surviving a close/reopen, while the scoped poll reconciles monitor/acquisition changes that do not
  // alter the owning Entity graph. Teardown stops all background reads while collapsed.
  $effect(() => {
    const key = cardsKey;
    if (!open || !key) return;

    void loadRows(key, { showLoading: true, waitForCurrent: true, reportErrors: true });
    const timer = setInterval(() => {
      void loadRows(key);
    }, POLL_INTERVAL_MS);
    return () => clearInterval(timer);
  });

  function expand(): void {
    open = !open;
  }

  async function loadRows(key = cardsKey, options: RefreshOptions = {}): Promise<void> {
    if (!key) return;
    if (refreshInFlight) {
      if (!options.waitForCurrent) return;
      await refreshInFlight;
      return loadRows(key, { ...options, waitForCurrent: false });
    }

    const refresh = readRows(key, options);
    refreshInFlight = refresh;
    try {
      await refresh;
    } finally {
      if (refreshInFlight === refresh) refreshInFlight = null;
    }
  }

  async function readRows(key: string, options: RefreshOptions): Promise<void> {
    if (options.showLoading) loading = true;
    if (options.reportErrors) error = null;
    try {
      const entityIds = cards.map((card) => card.entity.id);
      const states = await fetchEntityMonitorStates(entityIds);
      if (cardsKey !== key) return;

      const stateByEntity: Record<string, (typeof states)[number]> = Object.create(null);
      for (const state of states) {
        stateByEntity[state.entityId] = state;
      }

      const previousStatusByEntity = new Map(
        rows.map((row) => [row.card.entity.id, row.acquisition?.status ?? null]),
      );
      const nextRows = cards.map((card): ChildMonitoringRow => {
        const state = stateByEntity[card.entity.id];
        return {
          card,
          acquisition: state?.latestAcquisition ?? null,
          monitor: state?.monitor ?? null,
          canMonitor: state?.canMonitor ?? false,
          canRequest: state?.canRequest ?? false,
        };
      });
      rows = nextRows;

      const importedTransition = nextRows.some((row) => {
        const previous = previousStatusByEntity.get(row.card.entity.id);
        return previous !== undefined
          && previous !== null
          && ACTIVE_ACQUISITION_STATUSES.includes(previous)
          && row.acquisition?.status === ACQUISITION_STATUS.imported;
      });
      if (importedTransition) await onChanged?.();
    } catch (reason) {
      if (options.reportErrors) {
        error = messageFor(reason, "Failed to load child monitoring");
      }
    } finally {
      if (options.showLoading && cardsKey === key) loading = false;
    }
  }

  /**
   * Provider-backed Entity monitoring and graph-backed Wanted requests are separate capabilities.
   * Existing monitors remain controllable even if their metadata plugin is offline. Historical
   * acquisition rows are display state, never authority to create a new monitor.
   */
  function canSetMonitoring(row: ChildMonitoringRow): boolean {
    return row.monitor !== null || row.canMonitor || row.canRequest;
  }

  function isActive(row: ChildMonitoringRow): boolean {
    return monitorIsActive(row.monitor);
  }

  function isDeletingFiles(row: ChildMonitoringRow): boolean {
    return monitorIsDeletingFiles(row.monitor);
  }

  function isStopping(row: ChildMonitoringRow): boolean {
    return monitorIsStopping(row.monitor);
  }

  function hasUnknownMonitorStatus(row: ChildMonitoringRow): boolean {
    return monitorHasUnknownStatus(row.monitor);
  }

  function isAcquisitionStopping(row: ChildMonitoringRow): boolean {
    return ACQUISITION_STATUS.stopping !== undefined
      && row.acquisition?.status === ACQUISITION_STATUS.stopping;
  }

  function transitionLocked(row: ChildMonitoringRow): boolean {
    return isStopping(row)
      || isDeletingFiles(row)
      || hasUnknownMonitorStatus(row)
      || isAcquisitionStopping(row);
  }

  function isChecked(row: ChildMonitoringRow): boolean {
    return isActive(row) || isDeletingFiles(row) || hasUnknownMonitorStatus(row);
  }

  function rowStatus(row: ChildMonitoringRow): string {
    const acquisitionLabel = row.acquisition ? acquisitionLabelFor(row.acquisition) : null;
    if (isDeletingFiles(row)) return "Deleting files…";
    if (isAcquisitionStopping(row) || isStopping(row)) return "Stopping…";
    if (hasUnknownMonitorStatus(row)) return "Updating…";
    if (isActive(row)) return acquisitionLabel ? `${acquisitionLabel} · Monitoring` : "Monitoring";
    if (row.monitor) return acquisitionLabel ? `${acquisitionLabel} · Paused` : "Paused";
    if (isWanted(row.card.entity.capabilities)) return "Wanted";
    if (canSetMonitoring(row)) return acquisitionLabel ? `${acquisitionLabel} · Not monitored` : "Not monitored";
    return "On disk";
  }

  function acquisitionLabelFor(acquisition: AcquisitionSummary): string {
    if (acquisition.status === ACQUISITION_STATUS.stopping) return "Stopping…";
    if (acquisition.status === ACQUISITION_STATUS.imported) return "Imported";
    if (acquisition.status === ACQUISITION_STATUS.cancelled) return "Cancelled";
    return acquisitionStatusDisplay(acquisition.status).label;
  }

  async function setChildMonitoring(row: ChildMonitoringRow, monitored: boolean): Promise<void> {
    if (
      isDeletingFiles(row)
      || hasUnknownMonitorStatus(row)
      || isAcquisitionStopping(row)
      || (isStopping(row) && monitored)
    ) {
      throw new Error("Cleanup is still in progress.");
    }
    if (monitored) {
      await enableMonitoring(row);
      return;
    }
    if (row.monitor) await stopMonitor(row.monitor.id);
  }

  async function enableMonitoring(row: ChildMonitoringRow): Promise<void> {
    if (row.monitor) {
      if (!isActive(row)) await resumeMonitor(row.monitor.id);
      return;
    }
    // A fileless requestable leaf must commit first so its initial search starts immediately. It may
    // also be provider-monitorable, but a bare monitor waits for the periodic sweep. Source-backed
    // children without request work enter through the stable Entity/plugin identity monitor path.
    if (row.canRequest) {
      await commitEntityRequest(row.card.entity.id);
      return;
    }
    if (row.canMonitor) {
      await startEntityMonitor(row.card.entity.id);
      return;
    }
    throw new Error("This item cannot be monitored independently.");
  }

  async function toggleChild(row: ChildMonitoringRow, monitored: boolean): Promise<void> {
    if (acting) return;
    busyIds = [...busyIds, row.card.entity.id];
    error = null;
    outcome = null;
    try {
      try {
        await setChildMonitoring(row, monitored);
      } catch (reason) {
        error = `${row.card.entity.title}: ${messageFor(reason, "Failed to update monitoring")}`;
        return;
      }

      if (await reconcileAfterMutation(true)) {
        outcome = "Monitoring updated. Some page details will refresh shortly.";
      }
    } finally {
      busyIds = busyIds.filter((id) => id !== row.card.entity.id);
    }
  }

  async function applyToAll(monitored: boolean): Promise<void> {
    if (acting || loading) return;
    bulkBusy = true;
    error = null;
    outcome = null;
    const targets = actionableRows.filter((row) =>
      !transitionLocked(row) && (monitored ? !isActive(row) : row.monitor !== null),
    );
    let updated = 0;
    const failures: string[] = [];
    for (const row of targets) {
      try {
        await setChildMonitoring(row, monitored);
        updated += 1;
      } catch (reason) {
        failures.push(`${row.card.entity.title}: ${messageFor(reason, "Update failed")}`);
      }
    }

    const ownerRefreshFailed = await reconcileAfterMutation(updated > 0);
    bulkBusy = false;

    const result = failures.length > 0
      ? `${updated} updated; ${failures.length} failed. ${failures.join(" · ")}`
      : `${updated} ${updated === 1 ? "item" : "items"} updated.`;
    outcome = ownerRefreshFailed
      ? `${result} Some page details will refresh shortly.`
      : result;
  }

  /**
   * The local child snapshot is authoritative for this control and always refreshes after a mutation.
   * The owning page refresh is useful for counts/children but cannot turn an already-accepted server
   * mutation into a UI failure, so its rejection becomes only a deferred-refresh notice.
   */
  async function reconcileAfterMutation(notifyOwner: boolean): Promise<boolean> {
    const ownerRefresh = notifyOwner && onChanged
      ? Promise.resolve().then(() => onChanged())
      : Promise.resolve();
    const [, ownerResult] = await Promise.allSettled([
      loadRows(cardsKey, { waitForCurrent: true, reportErrors: true }),
      ownerRefresh,
    ]);
    return ownerResult.status === "rejected";
  }

  function messageFor(reason: unknown, fallback: string): string {
    return reason instanceof Error ? reason.message : fallback;
  }
</script>

{#if cards.length > 0}
  <section class="child-monitoring" aria-label="Child monitoring">
    <Button
      type="button"
      variant="ghost"
      size="md"
      class="h-auto w-full cursor-pointer justify-between rounded-none border-0 bg-transparent px-[0.8rem] py-[0.7rem] text-text-primary"
      aria-expanded={open}
      onclick={expand}
    >
      <span class="header-title">
        <Layers class="h-4 w-4 text-text-accent" />
        Child monitoring
        <span class="header-count">{cards.length}</span>
      </span>
      <ChevronDown class={cn("h-4 w-4 text-text-muted transition-transform", open && "rotate-180")} />
    </Button>

    {#if open}
      <div class="body">
        <div class="toolbar">
          <span class="toolbar-label">{childLabel}</span>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            disabled={loading || acting || !actionableRows.some((row) => !isActive(row) && !transitionLocked(row))}
            onclick={() => void applyToAll(true)}
          >
            Monitor all
          </Button>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            disabled={loading || acting || !rows.some((row) => row.monitor !== null && !transitionLocked(row))}
            onclick={() => void applyToAll(false)}
          >
            Unmonitor all
          </Button>
        </div>

        {#if error}<p class="message error">{error}</p>{/if}
        {#if outcome}<p class="message">{outcome}</p>{/if}

        {#if loading}
          <p class="loading"><Loader2 class="h-4 w-4 animate-spin" /> Loading monitoring state…</p>
        {:else}
          <ul class="rows">
            {#each rows as row (row.card.entity.id)}
              {@const href = resolveEntityThumbnailHref(row.card)}
              {@const active = isActive(row)}
              {@const checked = isChecked(row)}
              {@const busy = busyIds.includes(row.card.entity.id)}
              <li class="row">
                <span class="row-copy">
                  {#if href}
                    <a class="row-title" {href}>{row.card.entity.title}</a>
                  {:else}
                    <span class="row-title">{row.card.entity.title}</span>
                  {/if}
                  <span class:active class="row-status">{rowStatus(row)}</span>
                </span>
                {#if isStopping(row)}
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    disabled={acting || busy}
                    title={`Retry unmonitor cleanup for ${row.card.entity.title}`}
                    onclick={() => void toggleChild(row, false)}
                  >
                    Retry cleanup
                  </Button>
                {/if}
                <Toggle
                  checked={checked}
                  disabled={acting || busy || transitionLocked(row) || !canSetMonitoring(row)}
                  ariaLabel={`Monitor ${row.card.entity.title}`}
                  onchange={(value) => void toggleChild(row, value)}
                />
              </li>
            {/each}
          </ul>
        {/if}
      </div>
    {/if}
  </section>
{/if}

<style>
  .child-monitoring {
    display: flex;
    flex-direction: column;
    min-width: 0;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-sm);
    background: var(--color-surface-1);
  }
  .header-title {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    min-width: 0;
    font-family: var(--font-heading);
    font-size: 0.82rem;
    font-weight: 600;
  }
  .header-count,
  .toolbar-label,
  .row-status {
    font-family: var(--font-mono);
    font-size: 0.66rem;
  }
  .header-count,
  .row-status {
    color: var(--color-text-muted);
  }
  .body {
    display: grid;
    gap: 0.65rem;
    padding: 0 0.8rem 0.8rem;
  }
  .toolbar {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.35rem;
  }
  .toolbar-label {
    margin-right: auto;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    color: var(--color-text-secondary);
  }
  .rows {
    display: grid;
    margin: 0;
    padding: 0;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs);
    list-style: none;
  }
  .row {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    min-width: 0;
    padding: 0.55rem 0.65rem;
    border-bottom: 1px solid var(--color-border-subtle);
  }
  .row:last-child {
    border-bottom: 0;
  }
  .row-copy {
    display: grid;
    flex: 1;
    min-width: 0;
    gap: 0.12rem;
  }
  .row-title {
    overflow: hidden;
    color: var(--color-text-primary);
    font-size: 0.8rem;
    font-weight: 500;
    text-decoration: none;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  a.row-title:hover {
    color: var(--color-text-accent);
  }
  .row-status.active {
    color: var(--color-text-accent);
  }
  .loading {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    color: var(--color-text-muted);
    font-size: 0.78rem;
  }
  .message {
    margin: 0;
    color: var(--color-text-muted);
    font-size: 0.72rem;
  }
  .message.error {
    color: var(--color-error-text, #f87171);
  }
</style>
