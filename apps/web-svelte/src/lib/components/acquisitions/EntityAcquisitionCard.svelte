<script lang="ts">
  /**
   * THE acquisition + monitoring surface an entity page mounts — one card collapsing everything the
   * request layer knows about an entity: the standing container monitor (offered only when a metadata
   * plugin can track one of the entity's provider identities), the wanted placeholder's "Search for
   * release", and the full acquisition management panel (releases, live download, files, cancel).
   * Renders nothing for an ordinary owned entity with no acquisition story, so every entity page can
   * mount it unconditionally. Replaces the old hero-action hooks and EntityAcquisitionSection.
   */
  import { onDestroy } from "svelte";
  import { Bell, BellRing, CloudDownload, RefreshCw, Search } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import { MONITOR_STATUS } from "$lib/api/generated/codes";
  import type {
    AcquisitionDetail,
    EntityCapability,
    MonitorEligibilityView,
    MonitorView,
  } from "$lib/api/generated/model";
  import { firstProviderQualifiedId, isWanted } from "$lib/api/capabilities";
  import { fetchAcquisitionForEntity } from "$lib/api/acquisitions";
  import {
    fetchEntityMonitor,
    fetchMonitorEligibility,
    resumeMonitor,
    startEntityMonitor,
    stopMonitor,
  } from "$lib/api/monitors";
  import { commitEntityRequest, syncContainerRequest } from "$lib/api/requests";
  import AcquisitionPanel from "$lib/components/acquisitions/AcquisitionPanel.svelte";

  let {
    entityId,
    capabilities,
    onChanged,
    onCancelled,
  }: {
    entityId: string | null | undefined;
    capabilities?: EntityCapability[];
    /** Called after a state change (request started, monitor toggled, sync run) so the page can reload. */
    onChanged?: () => void | Promise<void>;
    /**
     * Called after the acquisition is cancelled. A wanted entity's page must navigate away here —
     * cancelling a request deletes its wanted placeholder, so the page it sat on no longer exists.
     */
    onCancelled?: () => void;
  } = $props();

  let acquisition = $state<AcquisitionDetail | null>(null);
  let monitor = $state<MonitorView | null>(null);
  let eligibility = $state<MonitorEligibilityView | null>(null);
  let monitorBusy = $state(false);
  let syncBusy = $state(false);
  let searchBusy = $state(false);
  let loadedId = $state<string | null>(null);
  let lastRequestedId = "";

  const wanted = $derived(!!capabilities && isWanted(capabilities));
  const monitorActive = $derived(monitor?.status === MONITOR_STATUS.active);
  const trackedVia = $derived(eligibility?.trackableProviders?.join(", ") ?? "");

  // The three blocks this card collapses. Container monitoring is offered only when the server says a
  // plugin can track the entity (or a monitor already exists and needs managing); the release search
  // only for a wanted phantom whose provider identity the server can resolve or degrade from.
  const showMonitor = $derived(monitor !== null || !!eligibility?.canMonitor);
  const showSearch = $derived(
    wanted && acquisition === null && !!capabilities && !!firstProviderQualifiedId(capabilities),
  );
  const visible = $derived(loadedId !== null && (showMonitor || showSearch || acquisition !== null));

  async function refresh(): Promise<void> {
    const id = entityId;
    if (!id) return;
    const [nextAcquisition, nextMonitor, nextEligibility] = await Promise.all([
      fetchAcquisitionForEntity(id).catch(() => null),
      fetchEntityMonitor(id).catch(() => null),
      fetchMonitorEligibility(id).catch(() => null),
    ]);
    // Ignore a stale load that resolved after the page moved to another entity.
    if (entityId !== id) return;
    acquisition = nextAcquisition;
    monitor = nextMonitor;
    eligibility = nextEligibility;
    loadedId = id;
  }

  $effect(() => {
    const id = entityId;
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
    void refresh();
  });

  // Acquisition state changes outside this page too — the Downloads table re-searches, a monitor
  // sweep starts a grab for a phantom, a request commits from Discover. Poll while the entity has an
  // acquisition story (wanted, monitored, monitorable, or an acquisition in hand) so the card stays
  // lock-step with the global Downloads view; an ordinary owned entity never polls. AcquisitionPanel
  // separately polls the fine-grained transfer state while a download is live.
  let pollTimer: ReturnType<typeof setInterval> | null = null;
  $effect(() => {
    const shouldPoll =
      !!entityId && loadedId !== null && (wanted || monitor !== null || !!eligibility?.canMonitor || acquisition !== null);
    if (shouldPoll && !pollTimer) {
      pollTimer = setInterval(() => void refresh(), 5000);
    } else if (!shouldPoll && pollTimer) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  });
  onDestroy(() => {
    if (pollTimer) clearInterval(pollTimer);
  });

  /** Toggle semantics mirror the acquisition panel: not monitored → start; paused → resume; active → stop. */
  async function toggleMonitor() {
    const id = entityId;
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
      await onChanged?.();
    } catch {
      // best-effort; the card reflects the last known state
    } finally {
      monitorBusy = false;
    }
  }

  /** Run the discovery sync now instead of waiting for the daily sweep. */
  async function syncNow() {
    const id = entityId;
    if (!id || syncBusy) return;
    syncBusy = true;
    try {
      await syncContainerRequest(id);
      await onChanged?.();
    } catch {
      // best-effort; the daily sweep covers it either way
    } finally {
      syncBusy = false;
    }
  }

  /** Requests this phantom: starts its auto-grabbing, monitored acquisition and refreshes the page. */
  async function searchForRelease() {
    const id = entityId;
    if (!id || searchBusy) return;
    searchBusy = true;
    try {
      await commitEntityRequest(id);
      await refresh();
      await onChanged?.();
    } catch {
      // best-effort; the page reflects the last known state
    } finally {
      searchBusy = false;
    }
  }
</script>

{#if visible}
  <section class="acquisition-card surface-panel">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 class="flex items-center gap-2 text-kicker text-text-primary">
        <CloudDownload class="h-3.5 w-3.5 text-text-accent" />
        Acquisition
      </h2>
      <div class="flex flex-wrap items-center gap-2">
        {#if monitorActive}
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={syncBusy}
            onclick={() => void syncNow()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
            title="Re-sync from the provider now instead of waiting for the daily sweep"
          >
            <RefreshCw class="h-3.5 w-3.5" />
            {syncBusy ? "Checking…" : "Check for new works"}
          </Button>
        {/if}
        {#if showMonitor}
          <Button
            type="button"
            variant={monitorActive ? "primary" : "secondary"}
            size="sm"
            disabled={monitorBusy}
            onclick={() => void toggleMonitor()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
            title={monitorActive
              ? "Watching for new works daily — click to stop"
              : "Watch this for new works; they appear as Wanted placeholders"}
          >
            {#if monitorActive}
              <BellRing class="h-3.5 w-3.5" />
              Monitoring
            {:else}
              <Bell class="h-3.5 w-3.5" />
              {monitor ? "Resume monitoring" : "Monitor"}
            {/if}
          </Button>
        {/if}
        {#if showSearch}
          <Button
            type="button"
            variant="primary"
            size="sm"
            disabled={searchBusy}
            onclick={() => void searchForRelease()}
            class="no-lift gap-1.5 px-2.5 py-1 text-xs"
          >
            <Search class="h-3.5 w-3.5" />
            {searchBusy ? "Searching…" : "Search for release"}
          </Button>
        {/if}
      </div>
    </div>

    {#if showMonitor && trackedVia}
      <p class="text-[0.72rem] text-text-muted">
        {monitorActive
          ? `Watching for new works daily via ${trackedVia}.`
          : `Monitoring available — tracked via ${trackedVia}.`}
      </p>
    {/if}

    {#if showSearch}
      <p class="text-[0.72rem] text-text-muted">
        No file yet. Searching starts an auto-grabbing, monitored acquisition for this item.
      </p>
    {/if}

    {#if acquisition}
      <AcquisitionPanel acquisitionId={acquisition.summary.id} bind:detail={acquisition} {onCancelled} />
    {/if}
  </section>
{/if}

<style>
  .acquisition-card {
    display: grid;
    gap: 0.9rem;
    padding: 1rem 1.1rem;
    min-width: 0;
  }
</style>
